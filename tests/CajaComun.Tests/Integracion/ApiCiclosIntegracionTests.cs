using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CajaComun.Application.Comun;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Infrastructure.Trabajos;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CajaComun.Tests.Integracion;

/// <summary>
/// Cierre de ciclo, dispersión y reportes por HTTP contra MariaDB real, con la cola de trabajos funcionando de verdad
/// (procesador incluido). Redis no se levanta: el estado de los trabajos va a memoria y el caché se desactiva.
/// </summary>
[Collection(ColeccionMariaDb.Nombre)]
[Trait("Category", Categorias.Integracion)]
public class ApiCiclosIntegracionTests(MariaDbFixture fx) : IAsyncLifetime
{
    private const string Clave = "Prueba-2024-x";

    private Entorno _a = null!;
    private Entorno _b = null!;
    private WebApplicationFactory<Program> _fabrica = null!;
    private HttpClient _http = null!;
    private string _admin = null!, _capturista = null!, _consultaB = null!;

    public async Task InitializeAsync()
    {
        _a = await Entorno.CrearAsync(fx);
        _b = await Entorno.CrearAsync(fx);
        var uno = await _a.AhorradorAsync(1_000m);
        await _a.AhorradorAsync(500m);
        await _a.PrestamoVigenteAsync(uno, 6_000m, 4);
        await _b.AhorradorAsync(800m);
        await _a.AplicarAnio2024Async();

        var hash = BCrypt.Net.BCrypt.HashPassword(Clave, 4);
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        await using (var db = fx.Crear())
        {
            (await db.Usuarios.FirstAsync(u => u.Id == _a.AdminId)).HashPassword = hash;
            db.Usuarios.AddRange(
                new Usuario { Nombre = "Capturista A", Correo = $"cap-{sufijo}@prueba.mx", HashPassword = hash, Rol = RolUsuario.Capturista,
                              Cajas = [new UsuarioCaja { CajaId = _a.CajaId }] },
                new Usuario { Nombre = "Consulta B", Correo = $"con-{sufijo}@prueba.mx", HashPassword = hash, Rol = RolUsuario.Consulta,
                              Cajas = [new UsuarioCaja { CajaId = _b.CajaId }] });
            await db.SaveChangesAsync();
        }

        _fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("MariaDb:Host", fx.Host);
            b.UseSetting("MariaDb:Port", fx.Puerto.ToString());
            b.UseSetting("MariaDb:Database", fx.Base);
            b.UseSetting("MariaDb:User", fx.Usuario);
            b.UseSetting("MariaDb:Password", fx.Clave);
            b.UseSetting("Jwt:Secret", TokenServiceTests.Secreto);
        b.UseSetting("Tareas:Habilitadas", "false");
            b.ConfigureServices(s =>
            {
                s.RemoveAll<IAlmacenTrabajos>();
                s.AddSingleton<IAlmacenTrabajos, AlmacenTrabajosMemoria>();
                s.RemoveAll<ICacheDatos>();
                s.AddSingleton<ICacheDatos, SinCache>();
            });
        });
        _http = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        string correoAdmin;
        await using (var db = fx.Crear()) correoAdmin = (await db.Usuarios.FirstAsync(u => u.Id == _a.AdminId)).Correo;
        _admin = await Login(correoAdmin);
        _capturista = await Login($"cap-{sufijo}@prueba.mx");
        _consultaB = await Login($"con-{sufijo}@prueba.mx");
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _fabrica.DisposeAsync();
    }

    private async Task<string> Login(string correo)
    {
        var r = await _http.PostAsJsonAsync("/api/v1/auth/login", new { correo, password = Clave });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return (await Json(r)).GetProperty("accessToken").GetString()!;
    }

    private async Task<HttpResponseMessage> Enviar(string token, HttpMethod metodo, string ruta, object? cuerpo = null)
    {
        var r = new HttpRequestMessage(metodo, ruta) { Content = cuerpo is null ? null : JsonContent.Create(cuerpo) };
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _http.SendAsync(r);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task<JsonElement> EsperarAsync(string token, string id)
    {
        for (var i = 0; i < 200; i++)
        {
            var t = await Json(await Enviar(token, HttpMethod.Get, $"/api/v1/trabajos/{id}"));
            if (t.GetProperty("estado").GetString() is "EXITO" or "ERROR") return t;
            await Task.Delay(50);
        }
        throw new TimeoutException("El trabajo no terminó.");
    }

    [Fact]
    public async Task El_cierre_completo_por_la_api_borrador_cierre_en_segundo_plano_dispersion_y_reportes()
    {
        // Paso 1 y 2: borrador.
        var borrador = await Enviar(_admin, HttpMethod.Post, "/api/v1/ciclos/borrador", new { cajaId = _a.CajaId, anio = 2024 });
        Assert.Equal(HttpStatusCode.OK, borrador.StatusCode);
        var resumen = await Json(borrador);
        var cicloId = resumen.GetProperty("ciclo").GetProperty("id").GetInt32();
        Assert.Equal("EN_CIERRE", resumen.GetProperty("ciclo").GetProperty("estado").GetString());
        Assert.True(resumen.GetProperty("puedeCerrar").GetBoolean());
        Assert.Equal(resumen.GetProperty("interesCobrado").GetDecimal(), resumen.GetProperty("interesDistribuido").GetDecimal());

        var distribucion = await Json(await Enviar(_admin, HttpMethod.Get, $"/api/v1/ciclos/{cicloId}/distribucion?pageSize=1"));
        Assert.Equal(2, distribucion.GetProperty("total").GetInt32());
        Assert.Equal(1, distribucion.GetProperty("items").GetArrayLength());

        // Cerrar: el rol Capturista no puede; el Administrador recibe 202 y consulta el trabajo.
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(_capturista, HttpMethod.Post, $"/api/v1/ciclos/{cicloId}/cerrar")).StatusCode);
        var aceptado = await Enviar(_admin, HttpMethod.Post, $"/api/v1/ciclos/{cicloId}/cerrar");
        Assert.Equal(HttpStatusCode.Accepted, aceptado.StatusCode);
        Assert.NotNull(aceptado.Headers.Location);
        var trabajo = await Json(aceptado);
        Assert.Equal("CIERRE_CICLO", trabajo.GetProperty("tipo").GetString());

        // El trabajo es del administrador: la capturista de la caja no lo ve.
        Assert.Equal(HttpStatusCode.NotFound, (await Enviar(_capturista, HttpMethod.Get, $"/api/v1/trabajos/{trabajo.GetProperty("id").GetString()}")).StatusCode);
        var fin = await EsperarAsync(_admin, trabajo.GetProperty("id").GetString()!);
        Assert.Equal("EXITO", fin.GetProperty("estado").GetString());
        Assert.Equal(2024, fin.GetProperty("resultado").GetProperty("anio").GetInt32());

        var cerrado = await Json(await Enviar(_admin, HttpMethod.Get, $"/api/v1/ciclos/{cicloId}"));
        Assert.Equal("CERRADO", cerrado.GetProperty("ciclo").GetProperty("estado").GetString());

        // Cerrarlo otra vez termina en error con su código estable.
        var otro = await Json(await Enviar(_admin, HttpMethod.Post, $"/api/v1/ciclos/{cicloId}/cerrar"));
        var finOtro = await EsperarAsync(_admin, otro.GetProperty("id").GetString()!);
        Assert.Equal(("ERROR", "CICLO_YA_CERRADO"), (finOtro.GetProperty("estado").GetString(), finOtro.GetProperty("codigoError").GetString()));

        // Paso 3: dispersión.
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(_capturista, HttpMethod.Post, $"/api/v1/ciclos/{cicloId}/layout-spei")).StatusCode);
        var spei = await Enviar(_admin, HttpMethod.Post, $"/api/v1/ciclos/{cicloId}/layout-spei?formato=BANORTE_SIMPLIFICADO&incluirAhorro=true");
        Assert.Equal(HttpStatusCode.OK, spei.StatusCode);
        Assert.EndsWith("-BANORTE.txt", spei.Content.Headers.ContentDisposition!.FileName);
        Assert.Equal("2", spei.Headers.GetValues("X-Registros").Single());
        Assert.Equal(64, spei.Headers.GetValues("X-Suma-Control").Single().Length);
        var historial = await Json(await Enviar(_admin, HttpMethod.Get, $"/api/v1/ciclos/{cicloId}/layouts"));
        Assert.Equal(1, historial.GetProperty("total").GetInt32());

        // Reportes en los tres formatos y por la cola.
        var pdf = await Enviar(_capturista, HttpMethod.Get, $"/api/v1/reportes/distribucion?cicloId={cicloId}&formato=PDF");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString((await pdf.Content.ReadAsByteArrayAsync())[..5]));
        var resultados = await Json(await Enviar(_capturista, HttpMethod.Get, $"/api/v1/reportes/estado-resultados?cajaId={_a.CajaId}&anio=2024"));
        Assert.Equal("CERRADO", resultados.GetProperty("estadoCiclo").GetString());

        var cola = await Json(await Enviar(_capturista, HttpMethod.Post, "/api/v1/reportes/trabajos",
            new { tipo = "ESTADO_RESULTADOS", formato = "XLSX", cajaId = _a.CajaId, anio = 2024 }));
        var finReporte = await EsperarAsync(_capturista, cola.GetProperty("id").GetString()!);
        Assert.Equal("EXITO", finReporte.GetProperty("estado").GetString());
        var archivo = await Enviar(_capturista, HttpMethod.Get, $"/api/v1/trabajos/{cola.GetProperty("id").GetString()}/archivo");
        Assert.Equal(HttpStatusCode.OK, archivo.StatusCode);
        Assert.Equal((byte)'P', (await archivo.Content.ReadAsByteArrayAsync())[0]);

        // El dashboard ya muestra el ciclo siguiente.
        var dash = await Json(await Enviar(_capturista, HttpMethod.Get, $"/api/v1/dashboard?cajaId={_a.CajaId}"));
        Assert.Equal(2025, dash.GetProperty("anioCiclo").GetInt32());
    }

    [Fact]
    public async Task Los_ciclos_y_reportes_de_otra_caja_estan_cerrados_para_quien_no_la_tiene()
    {
        await using (var db = fx.Crear()) await _a.Ciclos(db).BorradorAsync(new CajaComun.Application.Ciclos.BorradorCicloRequest(_a.CajaId, 2024));
        int cicloA;
        await using (var db = fx.Crear()) cicloA = (await db.Ciclos.FirstAsync(c => c.CajaId == _a.CajaId && c.Anio == 2024)).Id;

        Assert.Equal(HttpStatusCode.NotFound, (await Enviar(_consultaB, HttpMethod.Get, $"/api/v1/ciclos/{cicloA}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Enviar(_consultaB, HttpMethod.Get, $"/api/v1/ciclos/{cicloA}/distribucion")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(_consultaB, HttpMethod.Get, $"/api/v1/ciclos?cajaId={_a.CajaId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(_consultaB, HttpMethod.Get, $"/api/v1/reportes/estado-resultados?cajaId={_a.CajaId}&anio=2024")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(_consultaB, HttpMethod.Get, $"/api/v1/dashboard?cajaId={_a.CajaId}")).StatusCode);

        // Cerrar exige el rol de Administración: aquí se corta antes de tocar el ciclo ajeno.
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(_consultaB, HttpMethod.Post, $"/api/v1/ciclos/{cicloA}/cerrar")).StatusCode);

        var propios = await Json(await Enviar(_consultaB, HttpMethod.Get, "/api/v1/ciclos"));
        Assert.All(propios.EnumerateArray(), c => Assert.Equal(_b.CajaId, c.GetProperty("cajaId").GetInt32()));
    }
}

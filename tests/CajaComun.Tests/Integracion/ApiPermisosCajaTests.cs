using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CajaComun.Application.Consultas;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Infrastructure.Consultas;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CajaComun.Tests.Integracion;

/// <summary>
/// La pila completa (inicio de sesión, políticas por rol y alcance por caja) contra un MariaDB real. Dos cajas y
/// tres usuarios: un administrador, un capturista de la caja A y una consulta de la caja B.
/// </summary>
[Collection(ColeccionMariaDb.Nombre)]
[Trait("Category", Categorias.Integracion)]
public class ApiPermisosCajaTests(MariaDbFixture fx) : IAsyncLifetime
{
    private const string Clave = "Prueba-2024-x";

    private Entorno _a = null!;
    private Entorno _b = null!;
    private WebApplicationFactory<Program> _fabrica = null!;
    private HttpClient _http = null!;
    private string _admin = null!, _capturistaA = null!, _consultaB = null!;
    private string _correoCapturista = "", _correoConsulta = "";
    private int _ahorradorA, _ahorradorB, _prestamoB;
    private readonly TrinoFalso _trino = new();

    public async Task InitializeAsync()
    {
        _a = await Entorno.CrearAsync(fx);
        _b = await Entorno.CrearAsync(fx);
        _ahorradorA = (await _a.AhorradorAsync(1_000m, ahorro: 10_000m)).Id;
        var ahB = await _b.AhorradorAsync(1_000m, ahorro: 10_000m);
        _ahorradorB = ahB.Id;
        _prestamoB = (await _b.PrestamoVigenteAsync(ahB, 4_000m, 4)).Id;

        var hash = BCrypt.Net.BCrypt.HashPassword(Clave, 4);
        var sufijo = Guid.NewGuid().ToString("N")[..8];
        _correoCapturista = $"capturista-{sufijo}@prueba.mx";
        _correoConsulta = $"consulta-{sufijo}@prueba.mx";
        await using (var db = fx.Crear())
        {
            var admin = await db.Usuarios.FirstAsync(u => u.Id == _a.AdminId);
            admin.HashPassword = hash;
            db.Usuarios.AddRange(
                new Usuario { Nombre = "Capturista A", Correo = _correoCapturista, HashPassword = hash, Rol = RolUsuario.Capturista,
                              Cajas = [new UsuarioCaja { CajaId = _a.CajaId }] },
                new Usuario { Nombre = "Consulta B", Correo = _correoConsulta, HashPassword = hash, Rol = RolUsuario.Consulta,
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
            // La Parte 2 usa un Trino simulado: aquí se prueba el permiso y la paginación, no el motor.
            b.ConfigureServices(s => { s.RemoveAll<ITrinoClient>(); s.AddSingleton<ITrinoClient>(_trino); });
        });
        _http = _fabrica.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        await using (var db = fx.Crear())
        {
            _admin = await Login((await db.Usuarios.FirstAsync(u => u.Id == _a.AdminId)).Correo);
        }
        _capturistaA = await Login(_correoCapturista);
        _consultaB = await Login(_correoConsulta);
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

    private async Task<HttpResponseMessage> Enviar(string token, HttpMethod metodo, string ruta, HttpContent? contenido = null)
    {
        var r = new HttpRequestMessage(metodo, ruta) { Content = contenido };
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await _http.SendAsync(r);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<string?> Codigo(HttpResponseMessage r) => (await Json(r)).GetProperty("code").GetString();

    /// <summary>Trino simulado: el conteo responde 42 y la página trae dos filas.</summary>
    private sealed class TrinoFalso : ITrinoClient
    {
        public List<string> Consultas { get; } = [];

        public Task<ResultadoTrino> EjecutarAsync(string sql, bool reintentar = true, CancellationToken ct = default)
        {
            Consultas.Add(sql);
            if (sql.StartsWith("SELECT count(*) FROM (", StringComparison.Ordinal))
                return Task.FromResult(new ResultadoTrino([new("_col0", "bigint")], [[42L]], 1));
            return Task.FromResult(new ResultadoTrino(
                [new("caja", "varchar"), new("ahorrador", "varchar"), new("utilidad", "decimal(38,2)")],
                [["Caja A", "Ana, \"la\" Primera", "10.50"], ["Caja B", "=Beto", "0.00"]], 2));
        }
    }

    [Fact]
    public async Task Las_consultas_distribuidas_abarcan_todas_las_cajas_y_solo_las_ve_quien_tiene_acceso_a_todas()
    {
        foreach (var ruta in new[] { "/api/v1/consultas-distribuidas", "/api/v1/consultas-distribuidas/1", "/api/v1/consultas-distribuidas/1/csv", "/api/v1/consultas-distribuidas/1/equivalencia" })
        {
            foreach (var token in new[] { _capturistaA, _consultaB })
            {
                var r = await Enviar(token, HttpMethod.Get, ruta);
                Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
                Assert.Equal("CONSULTA_REQUIERE_TODAS_LAS_CAJAS", await Codigo(r));
            }
        }
        Assert.Empty(_trino.Consultas);   // ni siquiera se llegó a Trino
    }

    [Fact]
    public async Task El_administrador_obtiene_una_pagina_con_el_total_real_y_el_SQL_ejecutado()
    {
        var r = await Enviar(_admin, HttpMethod.Get, "/api/v1/consultas-distribuidas/4?anio=2024&page=3&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = await Json(r);
        Assert.Equal(42, json.GetProperty("total").GetInt32());
        Assert.Equal(3, json.GetProperty("page").GetInt32());
        Assert.Equal(10, json.GetProperty("pageSize").GetInt32());
        Assert.Equal(2024, json.GetProperty("anio").GetInt32());
        Assert.Equal(2, json.GetProperty("filas").GetArrayLength());
        Assert.Equal(["caja", "ahorrador", "utilidad"], json.GetProperty("columnas").EnumerateArray().Select(c => c.GetProperty("nombre").GetString()));

        var sql = json.GetProperty("sql").GetString()!;
        Assert.Contains("DATE '2024-01-01'", sql);
        Assert.DoesNotContain("{{", sql);
        Assert.Contains(_trino.Consultas, c => c.EndsWith("OFFSET 20 LIMIT 10", StringComparison.Ordinal));
        Assert.Contains(_trino.Consultas, c => c.StartsWith("SELECT count(*) FROM (", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Una_consulta_inexistente_o_un_anio_absurdo_se_rechazan_antes_de_llegar_a_Trino()
    {
        var r = await Enviar(_admin, HttpMethod.Get, "/api/v1/consultas-distribuidas/9");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal("CONSULTA_NO_ENCONTRADA", await Codigo(r));

        r = await Enviar(_admin, HttpMethod.Get, "/api/v1/consultas-distribuidas/4?anio=1999");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("VALIDACION", await Codigo(r));
        Assert.Empty(_trino.Consultas);
    }

    [Fact]
    public async Task El_CSV_lleva_encabezado_y_neutraliza_formulas()
    {
        var r = await Enviar(_admin, HttpMethod.Get, "/api/v1/consultas-distribuidas/5/csv?anio=2024");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.StartsWith("text/csv", r.Content.Headers.ContentType!.MediaType);
        Assert.Contains("consulta-q5-2024.csv", r.Content.Headers.ContentDisposition!.ToString());

        var bytes = await r.Content.ReadAsByteArrayAsync();
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);   // BOM para que Excel respete los acentos
        var texto = System.Text.Encoding.UTF8.GetString(bytes[3..]);
        var lineas = texto.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("caja,ahorrador,utilidad", lineas[0]);
        Assert.Equal("Caja A,\"Ana, \"\"la\"\" Primera\",10.50", lineas[1]);
        Assert.Equal("Caja B,'=Beto,0.00", lineas[2]);
    }

    [Fact]
    public async Task Cada_usuario_ve_solo_las_cajas_asignadas()
    {
        var cajas = await Json(await Enviar(_capturistaA, HttpMethod.Get, "/api/v1/cajas"));
        Assert.Equal([_a.CajaId], cajas.EnumerateArray().Select(c => c.GetProperty("id").GetInt32()));

        var deB = await Json(await Enviar(_consultaB, HttpMethod.Get, "/api/v1/cajas"));
        Assert.Equal([_b.CajaId], deB.EnumerateArray().Select(c => c.GetProperty("id").GetInt32()));

        var todas = (await Json(await Enviar(_admin, HttpMethod.Get, "/api/v1/cajas"))).EnumerateArray()
            .Select(c => c.GetProperty("id").GetInt32()).ToList();
        Assert.Contains(_a.CajaId, todas);
        Assert.Contains(_b.CajaId, todas);
    }

    [Fact]
    public async Task Una_caja_ajena_pedida_de_forma_explicita_responde_403_y_un_recurso_ajeno_por_id_404()
    {
        var r = await Enviar(_capturistaA, HttpMethod.Get, $"/api/v1/cajas/{_b.CajaId}/periodos");
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("CAJA_SIN_ACCESO", await Codigo(r));

        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(_capturistaA, HttpMethod.Get, $"/api/v1/ahorradores?cajaId={_b.CajaId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(_capturistaA, HttpMethod.Get, $"/api/v1/prestamos?cajaId={_b.CajaId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(_capturistaA, HttpMethod.Get, $"/api/v1/ahorro-voluntario?cajaId={_b.CajaId}")).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await Enviar(_capturistaA, HttpMethod.Get, $"/api/v1/ahorradores/{_ahorradorB}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Enviar(_capturistaA, HttpMethod.Get, $"/api/v1/prestamos/{_prestamoB}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Enviar(_capturistaA, HttpMethod.Get, $"/api/v1/nomina/periodos/{_b.Periodos[0].Id}/descuentos")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Enviar(_capturistaA, HttpMethod.Get, $"/api/v1/nomina/periodos/{_b.Periodos[0].Id}/incidencias")).StatusCode);
    }

    [Fact]
    public async Task Sin_caja_indicada_los_listados_solo_traen_lo_de_las_cajas_del_usuario()
    {
        var prestamos = await Json(await Enviar(_capturistaA, HttpMethod.Get, "/api/v1/prestamos"));
        Assert.Equal(0, prestamos.GetProperty("total").GetInt32());

        var deB = await Json(await Enviar(_consultaB, HttpMethod.Get, "/api/v1/prestamos"));
        Assert.Equal(1, deB.GetProperty("total").GetInt32());
        Assert.Equal(_prestamoB, deB.GetProperty("items")[0].GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Las_escrituras_sobre_una_caja_ajena_no_pasan_aunque_el_rol_lo_permita()
    {
        var aplicar = await Enviar(_capturistaA, HttpMethod.Post, $"/api/v1/nomina/periodos/{_a.Periodos[0].Id}/aplicar");
        Assert.Equal(HttpStatusCode.Forbidden, aplicar.StatusCode);                 // el rol Capturista no aplica periodos
        Assert.Equal("SIN_PERMISO", await Codigo(aplicar));

        var incidencia = new { ahorradorId = _ahorradorB, periodoId = _b.Periodos[0].Id, tipo = "LICENCIA" };
        Assert.Equal(HttpStatusCode.NotFound,
            (await Enviar(_capturistaA, HttpMethod.Post, "/api/v1/nomina/incidencias", JsonContent.Create(incidencia))).StatusCode);

        var archivo = new MultipartFormDataContent { { new StringContent(_b.CajaId.ToString()), "cajaId" } };
        archivo.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("movimiento|num_empleado\nALTA|TST-00099\n")), "archivo", "altas.txt");
        var carga = await Enviar(_capturistaA, HttpMethod.Post, "/api/v1/importaciones", archivo);
        Assert.Equal(HttpStatusCode.Forbidden, carga.StatusCode);
        Assert.Equal("CAJA_SIN_ACCESO", await Codigo(carga));

        var solicitud = new { ahorradorId = _ahorradorB, monto = 1_000, plazoQuincenas = 4 };
        Assert.Equal(HttpStatusCode.NotFound,
            (await Enviar(_capturistaA, HttpMethod.Post, "/api/v1/prestamos", JsonContent.Create(solicitud))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await Enviar(_capturistaA, HttpMethod.Put, $"/api/v1/ahorro-voluntario/{_ahorradorB}",
                JsonContent.Create(new { activo = true, cuotaVoluntaria = 100 }))).StatusCode);
    }

    [Fact]
    public async Task El_rol_limita_lo_que_se_puede_hacer_dentro_de_la_propia_caja()
    {
        // Consulta: ve su caja, pero no captura ni aplica.
        Assert.Equal(HttpStatusCode.OK,
            (await Enviar(_consultaB, HttpMethod.Get, $"/api/v1/nomina/periodos/{_b.Periodos[0].Id}/descuentos")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Enviar(_consultaB, HttpMethod.Post, "/api/v1/nomina/incidencias",
                JsonContent.Create(new { ahorradorId = _ahorradorB, periodoId = _b.Periodos[0].Id, tipo = "LICENCIA" }))).StatusCode);

        // Capturista: registra incidencias en su caja, pero no autoriza préstamos.
        var inc = await Enviar(_capturistaA, HttpMethod.Post, "/api/v1/nomina/incidencias",
            JsonContent.Create(new { ahorradorId = _ahorradorA, periodoId = _a.Periodos[0].Id, tipo = "LICENCIA" }));
        Assert.Equal(HttpStatusCode.Created, inc.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await Enviar(_capturistaA, HttpMethod.Post, $"/api/v1/prestamos/{_prestamoB}/autorizar")).StatusCode);
    }

    [Fact]
    public async Task El_administrador_aplica_el_periodo_de_cualquier_caja_y_el_resultado_es_idempotente()
    {
        var r = await Enviar(_admin, HttpMethod.Post, $"/api/v1/nomina/periodos/{_b.Periodos[0].Id}/aplicar");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var cuerpo = await Json(r);
        Assert.Equal("Q1-2024", cuerpo.GetProperty("referencia").GetString());
        Assert.True(cuerpo.GetProperty("movimientosGenerados").GetInt32() >= 3);          // ordinario + abonos del préstamo

        var otra = await Enviar(_admin, HttpMethod.Post, $"/api/v1/nomina/periodos/{_b.Periodos[0].Id}/aplicar");
        Assert.Equal(HttpStatusCode.Conflict, otra.StatusCode);
        Assert.Equal("PERIODO_YA_APLICADO", await Codigo(otra));
    }

    [Fact]
    public async Task Quitar_una_caja_a_un_usuario_surte_efecto_de_inmediato_sin_nuevo_token()
    {
        await using (var db = fx.Crear())
            await db.Database.ExecuteSqlRawAsync("DELETE FROM usuario_caja WHERE caja_id = {0}", _b.CajaId);

        // El token sigue vigente, pero el acceso se consulta a la base en cada petición.
        var r = await Enviar(_consultaB, HttpMethod.Get, $"/api/v1/nomina/periodos/{_b.Periodos[0].Id}/descuentos");
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
        Assert.Equal(0, (await Json(await Enviar(_consultaB, HttpMethod.Get, "/api/v1/cajas"))).GetArrayLength());
    }
}

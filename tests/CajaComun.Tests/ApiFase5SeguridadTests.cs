using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Infrastructure.Seguridad;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CajaComun.Tests;

/// <summary>Roles y validación de ciclos, reportes, tareas, trabajos y panel, sin llegar a la base de datos.</summary>
public class ApiFase5SeguridadTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _http = factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("MariaDb:Database", "prueba");
        b.UseSetting("MariaDb:User", "prueba");
        b.UseSetting("MariaDb:Password", "prueba");
        b.UseSetting("Jwt:Secret", TokenServiceTests.Secreto);
        b.UseSetting("Tareas:Habilitadas", "false");
    }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string Token(RolUsuario rol) =>
        new TokenService(new OpcionesJwt { Secreto = TokenServiceTests.Secreto })
            .CrearAcceso(new Usuario { Id = 7, Nombre = "Prueba", Correo = "p@x.mx", Rol = rol }, DateTime.UtcNow).Token;

    private static HttpRequestMessage Peticion(HttpMethod metodo, string ruta, RolUsuario? rol, object? cuerpo = null)
    {
        var r = new HttpRequestMessage(metodo, ruta);
        if (rol is { } valido) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(valido));
        if (cuerpo is not null) r.Content = JsonContent.Create(cuerpo);
        return r;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    [Theory]
    [InlineData("GET", "/api/v1/ciclos")]
    [InlineData("GET", "/api/v1/ciclos/1")]
    [InlineData("POST", "/api/v1/ciclos/borrador")]
    [InlineData("POST", "/api/v1/ciclos/1/cerrar")]
    [InlineData("POST", "/api/v1/ciclos/1/layout-spei")]
    [InlineData("GET", "/api/v1/reportes/estado-resultados?cajaId=1")]
    [InlineData("GET", "/api/v1/reportes/distribucion?cicloId=1")]
    [InlineData("POST", "/api/v1/reportes/trabajos")]
    [InlineData("GET", "/api/v1/tareas")]
    [InlineData("PUT", "/api/v1/tareas/1")]
    [InlineData("POST", "/api/v1/tareas/1/ejecutar")]
    [InlineData("GET", "/api/v1/trabajos/abc")]
    [InlineData("GET", "/api/v1/dashboard")]
    public async Task Sin_token_responde_401(string metodo, string ruta)
    {
        var r = await _http.SendAsync(Peticion(new HttpMethod(metodo), ruta, null));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("NO_AUTENTICADO", (await Json(r)).GetProperty("code").GetString());
    }

    [Theory]
    // Cerrar ciclos, dispersar y editar tareas es de Administración.
    [InlineData(RolUsuario.Capturista, "POST", "/api/v1/ciclos/borrador")]
    [InlineData(RolUsuario.Capturista, "POST", "/api/v1/ciclos/1/cerrar")]
    [InlineData(RolUsuario.Capturista, "POST", "/api/v1/ciclos/1/layout-spei")]
    [InlineData(RolUsuario.Consulta, "POST", "/api/v1/ciclos/borrador")]
    [InlineData(RolUsuario.Consulta, "POST", "/api/v1/ciclos/1/cerrar")]
    [InlineData(RolUsuario.Capturista, "PUT", "/api/v1/tareas/1")]
    [InlineData(RolUsuario.Capturista, "POST", "/api/v1/tareas/1/ejecutar")]
    [InlineData(RolUsuario.Consulta, "PUT", "/api/v1/tareas/1")]
    [InlineData(RolUsuario.Consulta, "POST", "/api/v1/tareas/1/ejecutar")]
    public async Task El_rol_sin_permiso_recibe_403(RolUsuario rol, string metodo, string ruta)
    {
        var r = await _http.SendAsync(Peticion(new HttpMethod(metodo), ruta, rol, new { }));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("SIN_PERMISO", (await Json(r)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(RolUsuario.Administrador, "/api/v1/ciclos/borrador")]
    [InlineData(RolUsuario.Consulta, "/api/v1/reportes/trabajos")]
    [InlineData(RolUsuario.Capturista, "/api/v1/reportes/trabajos")]
    public async Task Con_el_permiso_correcto_un_cuerpo_vacio_se_detiene_en_la_validacion(RolUsuario rol, string ruta)
    {
        var r = await _http.SendAsync(Peticion(HttpMethod.Post, ruta, rol, new { }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("VALIDACION", (await Json(r)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task El_anio_del_borrador_se_valida_con_su_codigo()
    {
        var r = await _http.SendAsync(Peticion(HttpMethod.Post, "/api/v1/ciclos/borrador", RolUsuario.Administrador, new { cajaId = 1, anio = 1999 }));
        Assert.Equal("ANIO_INVALIDO", (await Json(r)).GetProperty("errors").GetProperty("anio")[0].GetString());
    }

    [Fact]
    public async Task Un_reporte_en_segundo_plano_pide_los_datos_de_su_tipo()
    {
        var r = await _http.SendAsync(Peticion(HttpMethod.Post, "/api/v1/reportes/trabajos", RolUsuario.Consulta,
            new { tipo = "ESTADO_CUENTA", formato = "PDF" }));
        Assert.Equal("REQUERIDO", (await Json(r)).GetProperty("errors").GetProperty("ahorradorId")[0].GetString());
    }

    [Theory]
    [InlineData("/api/v1/ciclos/1/layout-spei?formato=SWIFT")]
    [InlineData("/api/v1/reportes/estado-resultados?cajaId=1&formato=DOC")]
    public async Task Un_formato_desconocido_responde_400(string ruta)
    {
        var metodo = ruta.Contains("layout-spei") ? HttpMethod.Post : HttpMethod.Get;
        var r = await _http.SendAsync(Peticion(metodo, ruta, RolUsuario.Administrador));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task La_frecuencia_de_una_tarea_debe_existir()
    {
        var r = await _http.SendAsync(Peticion(HttpMethod.Put, "/api/v1/tareas/1", RolUsuario.Administrador,
            new { frecuencia = "SEMESTRAL", hora = "02:00:00", activa = true }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }
}

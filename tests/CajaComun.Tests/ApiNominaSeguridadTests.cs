using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Infrastructure.Seguridad;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CajaComun.Tests;

/// <summary>
/// Roles y validación de nómina, importaciones, préstamos y ahorro voluntario a través del pipeline real. Igual que
/// en ApiSeguridadTests, ninguna petición llega a la base: la política y la validación responden antes.
/// </summary>
public class ApiNominaSeguridadTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
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

    private static HttpRequestMessage Peticion(HttpMethod metodo, string ruta, RolUsuario? rol, HttpContent? contenido = null)
    {
        var r = new HttpRequestMessage(metodo, ruta) { Content = contenido };
        if (rol is { } valido) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(valido));
        return r;
    }

    private static MultipartFormDataContent Archivo()
    {
        var c = new MultipartFormDataContent { { new StringContent("1"), "cajaId" } };
        c.Add(new ByteArrayContent("a"u8.ToArray()), "archivo", "altas.txt");
        return c;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    [Theory]
    [InlineData("GET", "/api/v1/nomina/periodos/1/descuentos")]
    [InlineData("GET", "/api/v1/nomina/periodos/1/incidencias")]
    [InlineData("POST", "/api/v1/nomina/periodos/1/aplicar")]
    [InlineData("POST", "/api/v1/nomina/periodos/1/layout")]
    [InlineData("GET", "/api/v1/nomina/layouts")]
    [InlineData("GET", "/api/v1/importaciones")]
    [InlineData("GET", "/api/v1/prestamos")]
    [InlineData("GET", "/api/v1/prestamos/resumen")]
    [InlineData("POST", "/api/v1/prestamos/simular")]
    [InlineData("POST", "/api/v1/prestamos/1/autorizar")]
    [InlineData("GET", "/api/v1/ahorro-voluntario")]
    [InlineData("PUT", "/api/v1/ahorro-voluntario/1")]
    public async Task Sin_token_responde_401(string metodo, string ruta)
    {
        var r = await _http.SendAsync(Peticion(new HttpMethod(metodo), ruta, null));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("NO_AUTENTICADO", (await Json(r)).GetProperty("code").GetString());
    }

    [Theory]
    // Consulta solo lee.
    [InlineData(RolUsuario.Consulta, "POST", "/api/v1/nomina/incidencias")]
    [InlineData(RolUsuario.Consulta, "DELETE", "/api/v1/nomina/incidencias/1")]
    [InlineData(RolUsuario.Consulta, "POST", "/api/v1/nomina/periodos/1/layout")]
    [InlineData(RolUsuario.Consulta, "POST", "/api/v1/nomina/periodos/1/aplicar")]
    [InlineData(RolUsuario.Consulta, "POST", "/api/v1/prestamos")]
    [InlineData(RolUsuario.Consulta, "POST", "/api/v1/prestamos/1/autorizar")]
    [InlineData(RolUsuario.Consulta, "POST", "/api/v1/prestamos/1/rechazar")]
    [InlineData(RolUsuario.Consulta, "PUT", "/api/v1/ahorro-voluntario/1")]
    [InlineData(RolUsuario.Consulta, "POST", "/api/v1/importaciones/1/aplicar")]
    [InlineData(RolUsuario.Consulta, "POST", "/api/v1/importaciones/1/cancelar")]
    // El capturista captura, pero aplicar el periodo y autorizar o rechazar préstamos es del administrador.
    [InlineData(RolUsuario.Capturista, "POST", "/api/v1/nomina/periodos/1/aplicar")]
    [InlineData(RolUsuario.Capturista, "POST", "/api/v1/prestamos/1/autorizar")]
    [InlineData(RolUsuario.Capturista, "POST", "/api/v1/prestamos/1/rechazar")]
    public async Task El_rol_sin_permiso_recibe_403(RolUsuario rol, string metodo, string ruta)
    {
        var r = await _http.SendAsync(Peticion(new HttpMethod(metodo), ruta, rol, JsonContent.Create(new { })));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("SIN_PERMISO", (await Json(r)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Consulta_no_puede_subir_archivos_de_importacion()
    {
        var r = await _http.SendAsync(Peticion(HttpMethod.Post, "/api/v1/importaciones", RolUsuario.Consulta, Archivo()));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Theory]
    [InlineData(RolUsuario.Capturista, "/api/v1/nomina/incidencias")]
    [InlineData(RolUsuario.Administrador, "/api/v1/nomina/incidencias")]
    [InlineData(RolUsuario.Capturista, "/api/v1/prestamos")]
    [InlineData(RolUsuario.Consulta, "/api/v1/prestamos/simular")]
    public async Task Con_el_permiso_correcto_un_cuerpo_vacio_se_detiene_en_la_validacion(RolUsuario rol, string ruta)
    {
        var r = await _http.SendAsync(Peticion(HttpMethod.Post, ruta, rol, JsonContent.Create(new { })));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var cuerpo = await Json(r);
        Assert.Equal("VALIDACION", cuerpo.GetProperty("code").GetString());
        Assert.True(cuerpo.GetProperty("errors").EnumerateObject().Any());
    }

    [Fact]
    public async Task Los_codigos_de_validacion_de_una_solicitud_de_prestamo_llegan_por_campo()
    {
        var cuerpo = new { ahorradorId = 1, monto = -10, plazoQuincenas = 99 };
        var r = await _http.SendAsync(Peticion(HttpMethod.Post, "/api/v1/prestamos/simular", RolUsuario.Consulta, JsonContent.Create(cuerpo)));
        var errores = (await Json(r)).GetProperty("errors");
        Assert.Equal("MONTO_INVALIDO", errores.GetProperty("monto")[0].GetString());
        Assert.Equal("PLAZO_INVALIDO", errores.GetProperty("plazoQuincenas")[0].GetString());
    }

    [Fact]
    public async Task El_voluntario_activo_sin_importe_se_rechaza_antes_de_la_base()
    {
        var r = await _http.SendAsync(Peticion(HttpMethod.Put, "/api/v1/ahorro-voluntario/1", RolUsuario.Capturista,
            JsonContent.Create(new { activo = true, cuotaVoluntaria = 0 })));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("MONTO_INVALIDO", (await Json(r)).GetProperty("errors").GetProperty("cuotaVoluntaria")[0].GetString());
    }

    [Fact]
    public async Task Un_formato_de_layout_desconocido_responde_400()
    {
        var r = await _http.SendAsync(Peticion(HttpMethod.Post, "/api/v1/nomina/periodos/1/layout?formato=PDF", RolUsuario.Capturista));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Subir_importacion_sin_archivo_responde_400()
    {
        var sinArchivo = new MultipartFormDataContent { { new StringContent("1"), "cajaId" } };
        var r = await _http.SendAsync(Peticion(HttpMethod.Post, "/api/v1/importaciones", RolUsuario.Capturista, sinArchivo));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }
}

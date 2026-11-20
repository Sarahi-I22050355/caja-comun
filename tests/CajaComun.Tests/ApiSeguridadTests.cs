using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Infrastructure.Seguridad;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CajaComun.Tests;

/// <summary>
/// Pruebas de autenticación, roles y validación a través del pipeline real. Ninguna llega a la base de datos:
/// la autorización y la validación responden antes de que un servicio abra conexión.
/// </summary>
public class ApiSeguridadTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _http = factory.WithWebHostBuilder(b =>
    {
        b.UseSetting("MariaDb:Database", "prueba");
        b.UseSetting("MariaDb:User", "prueba");
        b.UseSetting("MariaDb:Password", "prueba");
        b.UseSetting("Jwt:Secret", TokenServiceTests.Secreto);
        b.UseSetting("Tareas:Habilitadas", "false");
    }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static string Token(RolUsuario rol, DateTime? emitido = null) =>
        new TokenService(new OpcionesJwt { Secreto = TokenServiceTests.Secreto })
            .CrearAcceso(new Usuario { Id = 7, Nombre = "Prueba", Correo = "p@x.mx", Rol = rol }, emitido ?? DateTime.UtcNow).Token;

    private HttpRequestMessage Peticion(HttpMethod metodo, string ruta, RolUsuario? rol = null, object? cuerpo = null, string? token = null)
    {
        var r = new HttpRequestMessage(metodo, ruta);
        if (rol is { } rolValido || token is not null)
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token ?? Token(rol!.Value));
        if (cuerpo is not null) r.Content = JsonContent.Create(cuerpo);
        return r;
    }

    private static async Task<JsonElement> Json(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    [Theory]
    [InlineData("/api/v1/auth/me")]
    [InlineData("/api/v1/cajas")]
    [InlineData("/api/v1/ahorradores")]
    [InlineData("/api/v1/movimientos")]
    [InlineData("/api/v1/usuarios")]
    public async Task Sin_token_responde_401_con_codigo_estable(string ruta)
    {
        var r = await _http.SendAsync(Peticion(HttpMethod.Get, ruta));
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal("NO_AUTENTICADO", (await Json(r)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Un_token_vencido_o_alterado_se_rechaza()
    {
        var vencido = Token(RolUsuario.Administrador, DateTime.UtcNow.AddHours(-2));
        var r1 = await _http.SendAsync(Peticion(HttpMethod.Get, "/api/v1/cajas", token: vencido));
        Assert.Equal(HttpStatusCode.Unauthorized, r1.StatusCode);

        var alterado = Token(RolUsuario.Consulta);
        alterado = alterado[..^3] + (alterado.EndsWith("AAA") ? "BBB" : "AAA");
        var r2 = await _http.SendAsync(Peticion(HttpMethod.Get, "/api/v1/cajas", token: alterado));
        Assert.Equal(HttpStatusCode.Unauthorized, r2.StatusCode);
    }

    [Theory]
    [InlineData(RolUsuario.Consulta, "POST", "/api/v1/ahorradores")]
    [InlineData(RolUsuario.Consulta, "PUT", "/api/v1/ahorradores/1")]
    [InlineData(RolUsuario.Consulta, "POST", "/api/v1/ahorradores/1/cuotas")]
    [InlineData(RolUsuario.Consulta, "POST", "/api/v1/cajas")]
    [InlineData(RolUsuario.Consulta, "GET", "/api/v1/usuarios")]
    [InlineData(RolUsuario.Capturista, "POST", "/api/v1/cajas")]
    [InlineData(RolUsuario.Capturista, "PUT", "/api/v1/cajas/1")]
    [InlineData(RolUsuario.Capturista, "GET", "/api/v1/empresas")]
    [InlineData(RolUsuario.Capturista, "GET", "/api/v1/usuarios")]
    [InlineData(RolUsuario.Capturista, "POST", "/api/v1/usuarios")]
    [InlineData(RolUsuario.Capturista, "POST", "/api/v1/usuarios/1/password")]
    public async Task El_rol_sin_permiso_recibe_403(RolUsuario rol, string metodo, string ruta)
    {
        var r = await _http.SendAsync(Peticion(new HttpMethod(metodo), ruta, rol, cuerpo: new { }));
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        Assert.Equal("SIN_PERMISO", (await Json(r)).GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(RolUsuario.Capturista, "/api/v1/ahorradores")]
    [InlineData(RolUsuario.Administrador, "/api/v1/ahorradores")]
    [InlineData(RolUsuario.Administrador, "/api/v1/cajas")]
    [InlineData(RolUsuario.Administrador, "/api/v1/usuarios")]
    public async Task Con_el_permiso_correcto_la_autorizacion_pasa_y_la_validacion_responde(RolUsuario rol, string ruta)
    {
        // Cuerpo vacío: pasa la política y se detiene en la validación, sin tocar la base de datos.
        var r = await _http.SendAsync(Peticion(HttpMethod.Post, ruta, rol, cuerpo: new { }));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        var cuerpo = await Json(r);
        Assert.Equal("VALIDACION", cuerpo.GetProperty("code").GetString());
        Assert.True(cuerpo.GetProperty("errors").EnumerateObject().Any());
    }

    [Fact]
    public async Task La_validacion_devuelve_codigos_por_campo_en_camelCase()
    {
        var alta = new
        {
            cajaId = 1, nombre = "Jesús", apellidos = "Carreón", rfc = "MAL", fechaNacimiento = "1985-01-01",
            fechaIngreso = "2024-03-04", sueldoQuincenal = 9800, clabe = "002480701234567890", departamento = "Producción",
            cuotaOrdinaria = 900, cuotaVoluntaria = 0, voluntarioActivo = false
        };
        var r = await _http.SendAsync(Peticion(HttpMethod.Post, "/api/v1/ahorradores", RolUsuario.Capturista, alta));
        var errores = (await Json(r)).GetProperty("errors");

        Assert.Equal("RFC_INVALIDO", errores.GetProperty("rfc")[0].GetString());
        Assert.Equal("CLABE_INVALIDA", errores.GetProperty("clabe")[0].GetString());
        Assert.False(errores.TryGetProperty("nombre", out _));
    }

    [Fact]
    public async Task Un_enum_desconocido_en_la_consulta_responde_400_sin_llegar_a_la_base()
    {
        var r = await _http.SendAsync(Peticion(HttpMethod.Get, "/api/v1/ahorradores?estado=INVENTADO", RolUsuario.Consulta));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("VALOR_INVALIDO", (await Json(r)).GetProperty("errors").GetProperty("estado")[0].GetString());
    }

    [Fact]
    public async Task Un_enum_desconocido_en_el_cuerpo_responde_400()
    {
        var cuerpo = new { empresaId = 1, nombre = "Caja", prefijo = "CAJ", tasaInteresMensual = 1.5, frecuenciaNomina = "MENSUAL", anioCiclo = 2025 };
        var r = await _http.SendAsync(Peticion(HttpMethod.Post, "/api/v1/cajas", RolUsuario.Administrador, cuerpo));
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("VALIDACION", (await Json(r)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task El_login_valida_el_cuerpo_antes_de_consultar_usuarios()
    {
        var r = await _http.PostAsJsonAsync("/api/v1/auth/login", new { correo = "", password = "" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("VALIDACION", (await Json(r)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Refresh_sin_cookie_responde_401_y_logout_sin_cookie_responde_204()
    {
        var refresh = await _http.PostAsync("/api/v1/auth/refresh", null);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
        Assert.Equal("SESION_INVALIDA", (await Json(refresh)).GetProperty("code").GetString());

        var logout = await _http.PostAsync("/api/v1/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }

    [Fact]
    public async Task Las_respuestas_de_la_api_llevan_cabeceras_de_seguridad_y_no_se_cachean()
    {
        var r = await _http.SendAsync(Peticion(HttpMethod.Get, "/api/v1/cajas"));
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-store", r.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Los_health_checks_no_piden_sesion()
    {
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _http.GetAsync("/api/health")).StatusCode);
    }

    [Fact]
    public async Task Despues_de_8_intentos_de_login_por_minuto_responde_429()
    {
        // La validación de un cuerpo vacío no consume base de datos, pero el limitador ya contó la petición.
        HttpStatusCode ultimo = default;
        for (var i = 0; i < 12; i++)
            ultimo = (await _http.PostAsync("/api/v1/auth/login",
                new StringContent("{}", Encoding.UTF8, "application/json"))).StatusCode;
        Assert.Equal(HttpStatusCode.TooManyRequests, ultimo);
    }
}

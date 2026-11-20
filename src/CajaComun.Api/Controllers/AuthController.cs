using CajaComun.Api.Infraestructura;
using CajaComun.Application.Auth;
using CajaComun.Application.Comun;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CajaComun.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController(IAuthService auth) : ControllerBase
{
    private const string CookieRefresh = "caja_refresh";
    private const string RutaCookie = "/api/v1/auth";

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(Politicas.Login)]
    public async Task<ActionResult<SesionDto>> Login(LoginRequest request, CancellationToken ct)
    {
        var sesion = await auth.LoginAsync(request, ct);
        PonerCookie(sesion);
        return sesion.Sesion;
    }

    /// <summary>Cambia la cookie de refresco por un token de acceso nuevo (y rota la cookie).</summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<SesionDto>> Refrescar(CancellationToken ct)
    {
        if (!Request.Cookies.TryGetValue(CookieRefresh, out var token) || string.IsNullOrEmpty(token))
            throw new ErrorNegocio(Codigos.SesionInvalida, "No hay una sesión que renovar.", 401);

        try
        {
            var sesion = await auth.RefrescarAsync(token, ct);
            PonerCookie(sesion);
            return sesion.Sesion;
        }
        catch (ErrorNegocio)
        {
            BorrarCookie();
            throw;
        }
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        Request.Cookies.TryGetValue(CookieRefresh, out var token);
        await auth.CerrarSesionAsync(token, ct);
        BorrarCookie();
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<UsuarioActualDto>> Yo(CancellationToken ct) => await auth.ObtenerActualAsync(ct);

    // Secure sigue al esquema que ve la API (UseForwardedHeaders respeta X-Forwarded-Proto de Cloudflare/nginx).
    private void PonerCookie(SesionIniciada sesion) =>
        Response.Cookies.Append(CookieRefresh, sesion.RefreshToken, Opciones(sesion.RefreshExpira));

    private void BorrarCookie() => Response.Cookies.Delete(CookieRefresh, Opciones(null));

    private CookieOptions Opciones(DateTime? expira) => new()
    {
        HttpOnly = true,
        Secure = Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        Path = RutaCookie,
        Expires = expira is { } e ? new DateTimeOffset(DateTime.SpecifyKind(e, DateTimeKind.Utc)) : null,
        IsEssential = true
    };
}

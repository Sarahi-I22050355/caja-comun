using System.Security.Claims;
using System.Threading.RateLimiting;
using CajaComun.Application.Comun;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;
using CajaComun.Infrastructure.Seguridad;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CajaComun.Api.Infraestructura;

public static class Politicas
{
    public const string Lectura = "Lectura";
    public const string Captura = "Captura";
    public const string Administracion = "Administracion";
    public const string Login = "login";
    public const string Telemetria = "telemetria";
}

public static class ConfiguracionSeguridad
{
    public static IServiceCollection AddSeguridadApi(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<IUsuarioActual, UsuarioActualHttp>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<OpcionesJwt>((o, jwt) =>
            {
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true, ValidIssuer = OpcionesJwt.Emisor,
                    ValidateAudience = true, ValidAudience = OpcionesJwt.Audiencia,
                    ValidateIssuerSigningKey = true, IssuerSigningKey = jwt.Llave,
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = TokenService.ClaimNombre, RoleClaimType = TokenService.ClaimRol
                };
                o.Events = new JwtBearerEvents
                {
                    OnChallenge = async ctx =>
                    {
                        ctx.HandleResponse();
                        await Problemas.EscribirAsync(ctx.HttpContext, StatusCodes.Status401Unauthorized,
                            Codigos.NoAutenticado, "Inicia sesión para continuar.");
                    },
                    OnForbidden = ctx => Problemas.EscribirAsync(ctx.HttpContext, StatusCodes.Status403Forbidden,
                        Codigos.SinPermiso, "Tu rol no permite esta operación.")
                };
            });

        // Todo exige sesión salvo lo marcado con [AllowAnonymous]; los permisos por rol salen de Domain.Permisos.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Politicas.Lectura, PorAccion(AccionSistema.Leer))
            .AddPolicy(Politicas.Captura, PorAccion(AccionSistema.Capturar))
            .AddPolicy(Politicas.Administracion, PorAccion(AccionSistema.Administrar));

        // Freno a la fuerza bruta: pocos intentos de inicio de sesión por minuto desde cada IP.
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.AddPolicy(Politicas.Login, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 8, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            // Métricas web del navegador: ruta anónima, así que con tope por IP.
            o.AddPolicy(Politicas.Telemetria, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
            o.OnRejected = async (ctx, ct) =>
            {
                ctx.HttpContext.Response.Headers.RetryAfter = "60";
                await Problemas.EscribirAsync(ctx.HttpContext, StatusCodes.Status429TooManyRequests,
                    "DEMASIADOS_INTENTOS", "Demasiados intentos; espera un minuto.");
            };
        });
        return services;
    }

    private static AuthorizationPolicy PorAccion(AccionSistema accion) =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireAssertion(ctx => Enum.TryParse<RolUsuario>(ctx.User.FindFirstValue(TokenService.ClaimRol), out var rol)
                                     && Permisos.Puede(rol, accion))
            .Build();
}

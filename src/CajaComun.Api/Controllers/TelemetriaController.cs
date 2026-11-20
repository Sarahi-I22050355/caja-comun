using System.Diagnostics;
using System.Text.RegularExpressions;
using CajaComun.Api.Infraestructura;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace CajaComun.Api.Controllers;

/// <summary>
/// Métricas web de usuarios reales (monitoreo sencillo): el navegador reporta LCP, CLS, INP, FCP y TTFB con
/// <c>navigator.sendBeacon</c>, que no puede llevar el token, por eso la ruta es anónima. A cambio solo acepta valores
/// de una lista cerrada, no guarda nada de la persona y tiene límite de peticiones por IP.
/// </summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting(Politicas.Telemetria)]
[Route("api/v1/telemetria")]
public class TelemetriaController(ILogger<TelemetriaController> log) : ControllerBase
{
    private static readonly HashSet<string> Metricas = ["LCP", "CLS", "INP", "FCP", "TTFB"];
    private static readonly HashSet<string> Calificaciones = ["good", "needs-improvement", "poor"];

    // Rutas de la SPA (los ids se normalizan): una lista cerrada evita que el cliente invente etiquetas sin fin.
    private static readonly HashSet<string> Rutas =
    [
        "/", "/login", "/cajas", "/ahorradores", "/ahorradores/:id", "/sincronizacion", "/nomina", "/ahorro-voluntario",
        "/prestamos", "/prestamos/nuevo", "/prestamos/:id", "/cierre-ciclo", "/reportes", "/tareas", "/consultas", "/usuarios",
    ];

    private static readonly Regex Identificador = new(@"/\d+(?=/|$)", RegexOptions.Compiled);

    public sealed record VitalWeb(string? Nombre, double Valor, string? Ruta, string? Calificacion);

    [HttpPost("web-vitals")]
    [RequestSizeLimit(2_048)]
    public IActionResult WebVitals([FromBody] VitalWeb vital)
    {
        if (vital.Nombre is null || !Metricas.Contains(vital.Nombre) || !double.IsFinite(vital.Valor) || vital.Valor < 0 || vital.Valor > 600_000 || vital.Ruta?.Length > 200)
            return BadRequest();

        var ruta = Identificador.Replace(vital.Ruta ?? "", "/:id");
        if (!Rutas.Contains(ruta)) ruta = "otra";
        var calificacion = vital.Calificacion is not null && Calificaciones.Contains(vital.Calificacion) ? vital.Calificacion : "desconocida";

        MedidorCaja.VitalWeb.Record(vital.Valor, new TagList { { "metrica", vital.Nombre }, { "ruta", ruta }, { "calificacion", calificacion } });
        log.LogInformation("Web vital {Metrica} = {Valor:F2} en {Ruta} ({Calificacion})", vital.Nombre, vital.Valor, ruta, calificacion);
        return NoContent();
    }
}

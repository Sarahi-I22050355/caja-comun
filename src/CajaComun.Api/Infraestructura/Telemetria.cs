using System.Diagnostics;
using System.Diagnostics.Metrics;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace CajaComun.Api.Infraestructura;

/// <summary>Instrumentos propios: solicitudes lentas y métricas web de usuarios reales.</summary>
public static class MedidorCaja
{
    public const string Nombre = "CajaComun";
    public static readonly Meter Medidor = new(Nombre, "1.0");

    public static readonly Counter<long> SolicitudesLentas =
        Medidor.CreateCounter<long>("caja.solicitudes_lentas", description: "Solicitudes de la API que tardaron más que el umbral.");

    public static readonly Histogram<double> SolicitudMs =
        Medidor.CreateHistogram<double>("caja.solicitud.duracion", unit: "ms", description: "Duración de las solicitudes de la API por ruta.");

    public static readonly Histogram<double> VitalWeb =
        Medidor.CreateHistogram<double>("caja.web.vital", description: "Métricas web reportadas por el navegador (LCP, CLS, INP, FCP, TTFB).");
}

public static class ConfiguracionTelemetria
{
    /// <summary>
    /// Trazas de ASP.NET Core, de HttpClient (Trino) y de las consultas SQL (MySqlConnector), más las métricas propias.
    /// Exportador: OTLP si hay <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>; consola solo en desarrollo o con
    /// <c>Telemetria:Consola=true</c>; si no, no se exporta nada (las solicitudes lentas igual salen en el log).
    /// </summary>
    public static IServiceCollection AddTelemetria(this IServiceCollection services, IConfiguration config, IHostEnvironment entorno)
    {
        var otlp = !string.IsNullOrWhiteSpace(config["OTEL_EXPORTER_OTLP_ENDPOINT"]);
        var consola = config.GetValue("Telemetria:Consola", entorno.IsDevelopment());

        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("caja-comun-api"))
            .WithTracing(t =>
            {
                t.AddAspNetCoreInstrumentation(o =>
                    // /health lo consulta Docker cada pocos segundos: sería ruido.
                    o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health") && !ctx.Request.Path.StartsWithSegments("/api/health"));
                t.AddHttpClientInstrumentation();
                t.AddSource("MySqlConnector");
                if (otlp) t.AddOtlpExporter();
                if (consola) t.AddConsoleExporter();
            })
            .WithMetrics(m =>
            {
                m.AddAspNetCoreInstrumentation();
                m.AddMeter(MedidorCaja.Nombre);
                if (otlp) m.AddOtlpExporter();
            });
        return services;
    }

    /// <summary>
    /// Registra la duración de cada solicitud de la API y deja un aviso en el log (con la ruta, no con la URL) cuando pasa
    /// del umbral (500 ms por omisión, <c>Telemetria:UmbralLentoMs</c>): así se detecta cualquier endpoint lento.
    /// </summary>
    public static IApplicationBuilder UseSolicitudesLentas(this IApplicationBuilder app)
    {
        var config = app.ApplicationServices.GetRequiredService<IConfiguration>();
        var umbral = config.GetValue("Telemetria:UmbralLentoMs", 500);
        var log = app.ApplicationServices.GetRequiredService<ILoggerFactory>().CreateLogger("SolicitudesLentas");

        return app.Use(async (ctx, next) =>
        {
            var inicio = Stopwatch.GetTimestamp();
            try { await next(); }
            finally
            {
                if (ctx.Request.Path.StartsWithSegments("/api") && !ctx.Request.Path.StartsWithSegments("/api/health"))
                {
                    var ms = Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;
                    // La plantilla de la ruta (/api/v1/ahorradores/{id}) mantiene bajo el número de series.
                    var ruta = (ctx.GetEndpoint() as RouteEndpoint)?.RoutePattern.RawText ?? "sin-ruta";
                    var etiquetas = new TagList { { "metodo", ctx.Request.Method }, { "ruta", ruta } };
                    MedidorCaja.SolicitudMs.Record(ms, etiquetas);
                    if (ms > umbral)
                    {
                        MedidorCaja.SolicitudesLentas.Add(1, etiquetas);
                        log.LogWarning("Solicitud lenta {Metodo} {Ruta} respondió {Estado} en {Ms:F0} ms (umbral {Umbral} ms)",
                            ctx.Request.Method, ruta, ctx.Response.StatusCode, ms, umbral);
                    }
                }
            }
        });
    }
}

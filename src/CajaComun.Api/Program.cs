using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using CajaComun.Api.Infraestructura;
using CajaComun.Application.Comun;
using CajaComun.Infrastructure;
using CajaComun.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Serilog;

// Comandos de mantenimiento (seed, verificar-seed): corren y terminan sin levantar el servidor.
if (args.Length > 0 && CajaComun.Infrastructure.Persistence.Seed.ComandosSeed.Reconoce(args[0]))
    return await CajaComun.Infrastructure.Persistence.Seed.ComandosSeed.EjecutarAsync(args);

// Parte 2 (cargar-federacion, verificar-federacion): igual, corren y terminan.
if (args.Length > 0 && CajaComun.Infrastructure.Consultas.ComandosFederacion.Reconoce(args[0]))
    return await CajaComun.Infrastructure.Consultas.ComandosFederacion.EjecutarAsync(args);

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());

builder.Services
    .AddControllers(o =>
    {
        o.Filters.Add<FiltroValidacion>();
        o.ModelBinderProviders.Insert(0, new EnumDbBinderProvider());
    })
    .AddJsonOptions(o =>
    {
        // Los enums salen como códigos estables (ACTIVO, AHORRO_ORDINARIO), igual que en la base de datos.
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(new NombreEnumDb(), allowIntegerValues: false));
    })
    .ConfigureApiBehaviorOptions(o =>
    {
        // Datos mal formados (JSON roto, enum desconocido, fecha ilegible) también responden con código estable.
        o.InvalidModelStateResponseFactory = ctx =>
        {
            var errores = ctx.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .ToDictionary(
                    e => string.IsNullOrEmpty(e.Key) ? "_" : string.Join('.', e.Key.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName)),
                    _ => new[] { "VALOR_INVALIDO" });
            var pd = Problemas.Crear(ctx.HttpContext, StatusCodes.Status400BadRequest, Codigos.Validacion,
                "Hay datos que no son válidos.", errores);
            return new ObjectResult(pd) { StatusCode = pd.Status, ContentTypes = { "application/problem+json" } };
        };
    });

builder.Services.AddValidatorsFromAssemblyContaining<CajaComun.Application.Auth.LoginRequestValidator>();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddSeguridadApi();
builder.Services.AddTelemetria(builder.Configuration, builder.Environment);
builder.Services.AddExceptionHandler<ManejadorErrores>();
builder.Services.AddProblemDetails();

builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Optimal);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

// Detrás de nginx / Cloudflare Tunnel: se confía en X-Forwarded-* de la red interna.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseResponseCompression();
app.UseSerilogRequestLogging();
app.UseSolicitudesLentas();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    // Las respuestas de la API llevan datos de nómina y ahorro: no deben quedar en cachés compartidas.
    if (ctx.Request.Path.StartsWithSegments("/api")) ctx.Response.Headers.CacheControl = "no-store";
    await next();
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// /health es liveness (sin dependencias); /health/ready revisa MariaDB y Redis.
// Se exponen también bajo /api para que nginx los alcance con el mismo proxy.
foreach (var prefix in new[] { "", "/api" })
{
    app.MapHealthChecks($"{prefix}/health", new() { Predicate = _ => false }).AllowAnonymous();
    app.MapHealthChecks($"{prefix}/health/ready", new()
    {
        Predicate = r => r.Tags.Contains("ready"),
        ResponseWriter = async (ctx, report) =>
        {
            ctx.Response.ContentType = "application/json";
            var body = new
            {
                status = report.Status.ToString(),
                checks = report.Entries.ToDictionary(e => e.Key, e => e.Value.Status.ToString())
            };
            await ctx.Response.WriteAsync(JsonSerializer.Serialize(body));
        }
    }).AllowAnonymous();
}
app.MapControllers();

app.Run();
return 0;

public partial class Program;

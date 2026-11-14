using System.IO.Compression;
using System.Text.Json;
using CajaComun.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());

builder.Services.AddControllers();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddResponseCompression(o =>
{
    o.EnableForHttps = true;
    o.Providers.Add<BrotliCompressionProvider>();
    o.Providers.Add<GzipCompressionProvider>();
});
builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
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
app.UseResponseCompression();
app.UseSerilogRequestLogging();

// /health es liveness (sin dependencias); /health/ready revisa MariaDB y Redis.
// Se exponen también bajo /api para que nginx los alcance con el mismo proxy.
foreach (var prefix in new[] { "", "/api" })
{
    app.MapHealthChecks($"{prefix}/health", new() { Predicate = _ => false });
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
    });
}
app.MapControllers();

app.Run();

public partial class Program;


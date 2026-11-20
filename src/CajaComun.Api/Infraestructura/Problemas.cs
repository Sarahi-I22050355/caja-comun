using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace CajaComun.Api.Infraestructura;

/// <summary>Respuestas de error uniformes: ProblemDetails con un campo "code" estable que la interfaz traduce.</summary>
public static class Problemas
{
    public static ProblemDetails Crear(HttpContext ctx, int estado, string codigo, string detalle, IDictionary<string, string[]>? errores = null)
    {
        var pd = new ProblemDetails
        {
            Status = estado,
            Title = codigo,
            Detail = detalle,
            Type = $"https://httpstatuses.io/{estado}",
            Instance = ctx.Request.Path
        };
        pd.Extensions["code"] = codigo;
        pd.Extensions["traceId"] = ctx.TraceIdentifier;
        if (errores is { Count: > 0 }) pd.Extensions["errors"] = errores;
        return pd;
    }

    public static async Task EscribirAsync(HttpContext ctx, int estado, string codigo, string detalle, IDictionary<string, string[]>? errores = null)
    {
        var pd = Crear(ctx, estado, codigo, detalle, errores);
        ctx.Response.StatusCode = estado;
        ctx.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(ctx.Response.Body, pd, new JsonSerializerOptions(JsonSerializerDefaults.Web), ctx.RequestAborted);
    }
}

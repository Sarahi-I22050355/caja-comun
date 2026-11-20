using CajaComun.Application.Comun;
using Microsoft.AspNetCore.Diagnostics;

namespace CajaComun.Api.Infraestructura;

public sealed class ManejadorErrores(ILogger<ManejadorErrores> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is OperationCanceledException && ctx.RequestAborted.IsCancellationRequested)
        {
            // El cliente cortó la petición: no hay a quién responder ni error que registrar.
            ctx.Response.StatusCode = 499;
            return true;
        }

        if (ex is ErrorNegocio e)
        {
            await Problemas.EscribirAsync(ctx, e.Estado, e.Codigo, e.Message, e.Detalles);
            return true;
        }

        log.LogError(ex, "Error no controlado en {Metodo} {Ruta}", ctx.Request.Method, ctx.Request.Path);
        await Problemas.EscribirAsync(ctx, StatusCodes.Status500InternalServerError, Codigos.ErrorInterno,
            "Ocurrió un error inesperado.");
        return true;
    }
}

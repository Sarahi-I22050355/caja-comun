using System.Text.Json;
using CajaComun.Application.Comun;
using FluentValidation;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CajaComun.Api.Infraestructura;

/// <summary>
/// Corre el validador de FluentValidation de cada argumento de la acción que tenga uno registrado.
/// Los errores salen como códigos por campo, por ejemplo { "rfc": ["RFC_INVALIDO"] }.
/// </summary>
public sealed class FiltroValidacion(IServiceProvider servicios) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext contexto, ActionExecutionDelegate siguiente)
    {
        var errores = new Dictionary<string, List<string>>();

        foreach (var argumento in contexto.ActionArguments.Values)
        {
            if (argumento is null) continue;
            if (servicios.GetService(typeof(IValidator<>).MakeGenericType(argumento.GetType())) is not IValidator validador) continue;

            var resultado = await validador.ValidateAsync(new ValidationContext<object>(argumento), contexto.HttpContext.RequestAborted);
            foreach (var fallo in resultado.Errors)
            {
                var campo = string.IsNullOrEmpty(fallo.PropertyName)
                    ? "_"
                    : string.Join('.', fallo.PropertyName.Split('.').Select(JsonNamingPolicy.CamelCase.ConvertName));
                if (!errores.TryGetValue(campo, out var lista)) errores[campo] = lista = [];
                lista.Add(fallo.ErrorCode is { Length: > 0 } ? fallo.ErrorCode : "INVALIDO");
            }
        }

        if (errores.Count > 0)
            throw new ErrorNegocio(Codigos.Validacion, "Hay datos que no son válidos.", 400,
                errores.ToDictionary(x => x.Key, x => x.Value.Distinct().ToArray()));

        await siguiente();
    }
}

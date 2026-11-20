using System.Text.Json;
using CajaComun.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace CajaComun.Api.Infraestructura;

/// <summary>Lo mismo para los enums que llegan por query string o ruta.</summary>
public sealed class EnumDbBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext contexto)
    {
        var tipo = Nullable.GetUnderlyingType(contexto.Metadata.ModelType) ?? contexto.Metadata.ModelType;
        return tipo.IsEnum ? new EnumDbBinder(tipo) : null;
    }
}

internal sealed class EnumDbBinder(Type tipo) : IModelBinder
{
    private readonly Dictionary<string, object> _valores = Enum.GetNames(tipo)
        .ToDictionary(n => ConversionesEnum.ADb(n), n => Enum.Parse(tipo, n));

    public Task BindModelAsync(ModelBindingContext contexto)
    {
        var recibido = contexto.ValueProvider.GetValue(contexto.ModelName);
        if (recibido == ValueProviderResult.None) return Task.CompletedTask;

        contexto.ModelState.SetModelValue(contexto.ModelName, recibido);
        var texto = recibido.FirstValue;
        if (string.IsNullOrWhiteSpace(texto))
        {
            contexto.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        if (_valores.TryGetValue(texto.Trim().ToUpperInvariant(), out var valor))
            contexto.Result = ModelBindingResult.Success(valor);
        else
            contexto.ModelState.TryAddModelError(contexto.ModelName, "VALOR_INVALIDO");
        return Task.CompletedTask;
    }
}

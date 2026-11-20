using System.Text.Json;
using System.Text.Json.Serialization;

namespace CajaComun.Infrastructure.Persistence;

/// <summary>
/// Los enums viajan por la API igual que en la base de datos (ACTIVO, AHORRO_ORDINARIO, PAGARE_AVAL): son códigos
/// estables, no texto de interfaz. Esta política se usa en el JSON de la API y en el de los trabajos en segundo plano.
/// </summary>
public sealed class NombreEnumDb : JsonNamingPolicy
{
    public override string ConvertName(string name) => ConversionesEnum.ADb(name);

    /// <summary>Opciones con el mismo formato que la API: camelCase y enums como códigos.</summary>
    public static JsonSerializerOptions Opciones { get; } = Crear();

    private static JsonSerializerOptions Crear()
    {
        var o = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        o.Converters.Add(new JsonStringEnumConverter(new NombreEnumDb(), allowIntegerValues: false));
        return o;
    }
}

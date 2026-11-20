using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CajaComun.Infrastructure.Persistence;

public static partial class ConversionesEnum
{
    /// <summary>AhorroOrdinario -> AHORRO_ORDINARIO, Ahorro80 -> AHORRO_80.</summary>
    public static string ADb(string nombre) =>
        SeparadorMayusculas.Replace(nombre, "_").ToUpperInvariant();

    public static string ADb<T>(T valor) where T : struct, Enum => ADb(valor.ToString());

    /// <summary>Operación inversa: AHORRO_ORDINARIO -> AhorroOrdinario (para leer filas de Dapper).</summary>
    public static T DeDb<T>(string valor) where T : struct, Enum => Inversos<T>.Valores[valor];

    public static bool TryDeDb<T>(string? valor, out T resultado) where T : struct, Enum
    {
        resultado = default;
        return valor is not null && Inversos<T>.Valores.TryGetValue(valor.ToUpperInvariant(), out resultado);
    }

    private static class Inversos<T> where T : struct, Enum
    {
        public static readonly Dictionary<string, T> Valores = Enum.GetValues<T>().ToDictionary(v => ADb(v));
    }

    /// <summary>Columna ENUM de MariaDB con los valores del enum de C#.</summary>
    public static PropertyBuilder<T> HasEnumColumn<T>(this PropertyBuilder<T> propiedad) where T : struct, Enum
    {
        var valores = Enum.GetValues<T>().ToDictionary(v => ADb(v), v => v);
        var conversor = new ValueConverter<T, string>(v => ADb(v), s => valores[s]);
        return propiedad
            .HasConversion(conversor)
            .HasColumnType($"enum({string.Join(",", valores.Keys.Select(k => $"'{k}'"))})");
    }

    private static readonly Regex SeparadorMayusculas = new("(?<=[a-z])(?=[A-Z0-9])", RegexOptions.Compiled);
}

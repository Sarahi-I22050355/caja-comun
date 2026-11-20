using System.Text.RegularExpressions;

namespace CajaComun.Domain.Reglas;

public static class ReglasAhorrador
{
    private static readonly Regex PatronRfc = new("^[A-ZÑ&]{4}[0-9]{6}[A-Z0-9]{3}$", RegexOptions.Compiled);

    /// <summary>RFC de persona física: 4 letras, fecha AAMMDD y homoclave de 3 caracteres.</summary>
    public static bool RfcValido(string? rfc) => rfc is not null && PatronRfc.IsMatch(rfc);

    /// <summary>Número de empleado de la caja: prefijo, guion y consecutivo de 5 dígitos (GIM-00123).</summary>
    public static string NumeroEmpleado(string prefijo, int consecutivo) => $"{prefijo}-{consecutivo:00000}";

}

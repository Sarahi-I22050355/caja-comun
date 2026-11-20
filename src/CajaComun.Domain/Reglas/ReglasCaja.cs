namespace CajaComun.Domain.Reglas;

public static class ReglasCaja
{
    public const decimal TasaMinima = 1.00m;
    public const decimal TasaMaxima = 2.00m;

    /// <summary>Tasa mensual entre 1.00% y 2.00% con a lo más dos decimales (la columna es DECIMAL(5,2)).</summary>
    public static bool TasaValida(decimal tasaMensual) =>
        tasaMensual is >= TasaMinima and <= TasaMaxima && decimal.Round(tasaMensual, 2) == tasaMensual;
}

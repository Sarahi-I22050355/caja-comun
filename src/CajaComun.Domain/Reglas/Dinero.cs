namespace CajaComun.Domain.Reglas;

public static class Dinero
{
    /// <summary>Redondeo a 2 decimales con el criterio comercial (0.5 se aleja de cero).</summary>
    public static decimal Redondear(decimal valor) =>
        Math.Round(valor, 2, MidpointRounding.AwayFromZero);
}

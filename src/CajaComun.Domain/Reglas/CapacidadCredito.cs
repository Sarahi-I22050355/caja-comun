namespace CajaComun.Domain.Reglas;

/// <summary>
/// Tope de crédito: hasta el 80% del ahorro acumulado sin garantía adicional. Si el monto lo excede
/// se requiere pagaré y aval (el aval debe cubrir el excedente con su propio disponible).
/// </summary>
public static class CapacidadCredito
{
    public const decimal PorcentajeLimite = 0.80m;

    public static decimal Limite(decimal ahorroAcumulado) =>
        Dinero.Redondear(Math.Max(0m, ahorroAcumulado) * PorcentajeLimite);

    public static decimal DisponibleSinAval(decimal ahorroAcumulado, decimal saldoPrestamosVigentes) =>
        Math.Max(0m, Limite(ahorroAcumulado) - saldoPrestamosVigentes);

    public static bool RequiereAval(decimal monto, decimal ahorroAcumulado, decimal saldoPrestamosVigentes) =>
        monto > DisponibleSinAval(ahorroAcumulado, saldoPrestamosVigentes);

    /// <summary>Parte del monto que el propio ahorro no cubre y debe respaldar el aval.</summary>
    public static decimal ExcedenteAval(decimal monto, decimal ahorroAcumulado, decimal saldoPrestamosVigentes) =>
        Math.Max(0m, monto - DisponibleSinAval(ahorroAcumulado, saldoPrestamosVigentes));
}

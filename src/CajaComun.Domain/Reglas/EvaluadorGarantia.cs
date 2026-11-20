using CajaComun.Domain.Enums;

namespace CajaComun.Domain.Reglas;

public sealed record EvaluacionGarantia(GarantiaPrestamo Garantia, bool Aprobada, decimal ExcedenteAval, string? Motivo);

/// <summary>
/// Garantía de un préstamo. Hasta el 80% del ahorro (menos lo que ya se debe) basta con el ahorro; si el monto
/// lo excede se exige pagaré con aval, otro ahorrador de la misma caja cuyo disponible cubra el excedente.
/// </summary>
public static class EvaluadorGarantia
{
    public const string AvalRequerido = "AVAL_REQUERIDO";
    public const string AvalInsuficiente = "AVAL_INSUFICIENTE";

    public static EvaluacionGarantia Evaluar(
        decimal monto, decimal ahorroSolicitante, decimal saldoPrestamosSolicitante, decimal? disponibleAval)
    {
        var excedente = CapacidadCredito.ExcedenteAval(monto, ahorroSolicitante, saldoPrestamosSolicitante);
        if (excedente == 0m)
            return new EvaluacionGarantia(GarantiaPrestamo.Ahorro80, true, 0m, null);

        if (disponibleAval is null)
            return new EvaluacionGarantia(GarantiaPrestamo.PagareAval, false, excedente, AvalRequerido);

        return disponibleAval.Value >= excedente
            ? new EvaluacionGarantia(GarantiaPrestamo.PagareAval, true, excedente, null)
            : new EvaluacionGarantia(GarantiaPrestamo.PagareAval, false, excedente, AvalInsuficiente);
    }

    /// <summary>
    /// Lo que un ahorrador puede respaldar como aval: su disponible propio menos los excedentes que ya avala
    /// en otros préstamos vigentes (no se puede comprometer dos veces el mismo ahorro).
    /// </summary>
    public static decimal DisponibleComoAval(decimal ahorroAval, decimal saldoPrestamosAval, decimal excedentesYaAvalados) =>
        Math.Max(0m, CapacidadCredito.DisponibleSinAval(ahorroAval, saldoPrestamosAval) - excedentesYaAvalados);
}

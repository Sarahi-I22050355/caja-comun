namespace CajaComun.Domain.Reglas;

public sealed record CuotaAmortizacion(
    int Numero, decimal SaldoInicial, decimal Capital, decimal Interes, decimal Total, decimal SaldoFinal);

public static class CalculadoraAmortizacion
{
    /// <summary>
    /// Capital constante: monto / plazo por quincena. El interés de cada cuota es el saldo insoluto
    /// inicial por la mitad de la tasa mensual. La última cuota absorbe el residuo de capital.
    /// </summary>
    public static IReadOnlyList<CuotaAmortizacion> Generar(
        decimal monto, int plazoQuincenas, decimal tasaMensualPorcentaje, decimal periodosPorMes = 2m)
    {
        if (monto <= 0) throw new ArgumentOutOfRangeException(nameof(monto), "El monto debe ser mayor a cero.");
        if (plazoQuincenas <= 0) throw new ArgumentOutOfRangeException(nameof(plazoQuincenas), "El plazo debe ser mayor a cero.");
        if (tasaMensualPorcentaje < 0) throw new ArgumentOutOfRangeException(nameof(tasaMensualPorcentaje));
        if (periodosPorMes <= 0) throw new ArgumentOutOfRangeException(nameof(periodosPorMes));

        var tasaQuincenal = tasaMensualPorcentaje / 100m / periodosPorMes;
        var capitalBase = Dinero.Redondear(monto / plazoQuincenas);
        var cuotas = new List<CuotaAmortizacion>(plazoQuincenas);
        var saldo = monto;

        for (var n = 1; n <= plazoQuincenas; n++)
        {
            var capital = n == plazoQuincenas ? saldo : capitalBase;
            var interes = Dinero.Redondear(saldo * tasaQuincenal);
            var saldoFinal = saldo - capital;
            cuotas.Add(new CuotaAmortizacion(n, saldo, capital, interes, capital + interes, saldoFinal));
            saldo = saldoFinal;
        }
        return cuotas;
    }
}

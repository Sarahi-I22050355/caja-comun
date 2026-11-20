namespace CajaComun.Domain.Reglas;

public sealed record CambioAhorro(DateOnly Fecha, decimal Monto);

public sealed record ResultadoSaldoDias(decimal SaldoDias, int DiasActivos)
{
    public decimal SaldoPromedio => DiasActivos == 0 ? 0m : Dinero.Redondear(SaldoDias / DiasActivos);
}

public static class SaldoDias
{
    /// <summary>
    /// Saldo-días del ciclo. Una aportación cuenta desde el día en que se hace; una liquidación surte
    /// efecto al terminar su día (por eso un ciclo completo suma 365 días y quien se da de baja cuenta
    /// hasta su fecha de baja). Los días activos son los que tuvieron saldo positivo.
    /// </summary>
    public static ResultadoSaldoDias Calcular(
        IEnumerable<CambioAhorro> aportaciones, IEnumerable<CambioAhorro> liquidaciones,
        DateOnly inicio, DateOnly fin)
    {
        decimal saldoDias = 0;
        DateOnly? primera = null;
        foreach (var a in aportaciones.Where(a => a.Fecha >= inicio && a.Fecha <= fin))
        {
            saldoDias += a.Monto * (fin.DayNumber - a.Fecha.DayNumber + 1);
            if (primera is null || a.Fecha < primera) primera = a.Fecha;
        }
        if (primera is null) return new ResultadoSaldoDias(0, 0);

        DateOnly? ultima = null;
        foreach (var l in liquidaciones.Where(l => l.Fecha >= inicio && l.Fecha <= fin))
        {
            saldoDias -= l.Monto * (fin.DayNumber - l.Fecha.DayNumber);
            if (ultima is null || l.Fecha > ultima) ultima = l.Fecha;
        }

        var hasta = ultima ?? fin;
        return new ResultadoSaldoDias(saldoDias, hasta.DayNumber - primera.Value.DayNumber + 1);
    }
}

namespace CajaComun.Domain.Reglas;

/// <summary>Reglas del cierre de ciclo que no dependen de la base de datos.</summary>
public static class ReglasCiclo
{
    /// <summary>
    /// Ahorro que se conserva al cerrar el ciclo para respaldar los préstamos que siguen vigentes: lo necesario para que
    /// el saldo de esos préstamos siga siendo, a lo más, el 80% del ahorro (saldo / 0.80, al centavo hacia arriba),
    /// sin pasar de lo que el ahorrador tiene.
    /// </summary>
    public static decimal AhorroRetenido(decimal ahorroAlCorte, decimal saldoPrestamosVigentes)
    {
        if (ahorroAlCorte <= 0m || saldoPrestamosVigentes <= 0m) return 0m;
        var necesario = Math.Ceiling(saldoPrestamosVigentes / CapacidadCredito.PorcentajeLimite * 100m) / 100m;
        return Math.Min(ahorroAlCorte, necesario);
    }

    /// <summary>Siguiente día hábil (lunes a viernes) después de la fecha dada; no se consideran días festivos.</summary>
    public static DateOnly SiguienteDiaHabil(DateOnly fecha)
    {
        var d = fecha.AddDays(1);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
        return d;
    }

    /// <summary>Las dispersiones del ciclo: lo liquidado más el rendimiento, o solo el rendimiento.</summary>
    public static decimal ImporteDispersion(decimal ahorroLiquidado, decimal interesAsignado, bool incluirAhorro) =>
        (incluirAhorro ? ahorroLiquidado : 0m) + interesAsignado;
}

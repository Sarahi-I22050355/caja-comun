using CajaComun.Domain.Enums;

namespace CajaComun.Domain.Reglas;

public sealed record CuotaVigente(decimal Ordinaria, decimal Voluntaria, bool VoluntarioActivo);

/// <summary>El voluntario va siempre en su propio renglón: nunca se suma a la cuota ordinaria.</summary>
public sealed record DescuentoPeriodo(decimal Ordinaria, decimal Voluntaria, decimal Prestamo, bool Suspendido)
{
    public decimal Total => Ordinaria + Voluntaria + Prestamo;
}

public static class CalculadoraDescuento
{
    private static readonly TipoIncidencia[] SuspendenTodo =
        [TipoIncidencia.Licencia, TipoIncidencia.Incapacidad, TipoIncidencia.Baja];

    /// <summary>
    /// Descuento de una quincena = ordinaria + voluntaria (si está activa) + cuota de préstamo.
    /// Licencia, incapacidad o baja suspenden todo el descuento; la suspensión del voluntario solo ese concepto.
    /// Debe dar lo mismo que la vista v_descuento_periodo.
    /// </summary>
    public static DescuentoPeriodo Calcular(CuotaVigente cuota, decimal cuotaPrestamo, IEnumerable<TipoIncidencia> incidencias)
    {
        var lista = incidencias as IReadOnlyCollection<TipoIncidencia> ?? incidencias.ToList();
        if (lista.Any(SuspendenTodo.Contains))
            return new DescuentoPeriodo(0m, 0m, 0m, Suspendido: true);

        var voluntario = cuota.VoluntarioActivo && !lista.Contains(TipoIncidencia.SuspendeVoluntario)
            ? cuota.Voluntaria
            : 0m;
        return new DescuentoPeriodo(cuota.Ordinaria, voluntario, cuotaPrestamo, Suspendido: false);
    }
}

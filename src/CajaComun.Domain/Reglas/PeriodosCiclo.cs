using CajaComun.Domain.Enums;

namespace CajaComun.Domain.Reglas;

public sealed record RangoPeriodo(int Numero, DateOnly Inicio, DateOnly Fin);

public static class PeriodosCiclo
{
    public const int SemanasPorAnio = 52;

    /// <summary>
    /// Periodos de nómina del año natural. Quincenal: Q1..Q24. Semanal: 52 bloques de 7 días desde el 1 de enero;
    /// el último absorbe los días sobrantes (1 o 2) para que el año quede cubierto sin huecos.
    /// </summary>
    public static IReadOnlyList<RangoPeriodo> Generar(FrecuenciaNomina frecuencia, int anio)
    {
        if (frecuencia == FrecuenciaNomina.Quincenal)
        {
            return Enumerable.Range(1, Quincenas.PorAnio)
                .Select(n => { var (i, f) = Quincenas.Rango(anio, n); return new RangoPeriodo(n, i, f); })
                .ToList();
        }

        var primero = new DateOnly(anio, 1, 1);
        var ultimoDia = new DateOnly(anio, 12, 31);
        return Enumerable.Range(1, SemanasPorAnio)
            .Select(n =>
            {
                var inicio = primero.AddDays(7 * (n - 1));
                var fin = n == SemanasPorAnio ? ultimoDia : inicio.AddDays(6);
                return new RangoPeriodo(n, inicio, fin);
            })
            .ToList();
    }
}

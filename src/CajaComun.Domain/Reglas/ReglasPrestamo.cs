using CajaComun.Domain.Enums;

namespace CajaComun.Domain.Reglas;

/// <summary>Reglas de préstamos que no dependen de la base de datos.</summary>
public static class ReglasPrestamo
{
    public const int PlazoMinimo = 1;
    /// <summary>Número máximo de cuotas (periodos de nómina) de un préstamo; 36 quincenas son 18 meses.</summary>
    public const int PlazoMaximo = 36;

    public static bool PlazoValido(int plazo) => plazo is >= PlazoMinimo and <= PlazoMaximo;

    /// <summary>
    /// Las operaciones se fechan dentro del primer periodo abierto: si hoy cae fuera de él (por ejemplo, una base
    /// con datos de un ejercicio anterior) se usa el límite más cercano para no mezclar líneas de tiempo.
    /// </summary>
    public static DateOnly FechaOperacion(DateOnly hoy, DateOnly inicioPeriodo, DateOnly finPeriodo) =>
        hoy < inicioPeriodo ? inicioPeriodo : hoy > finPeriodo ? finPeriodo : hoy;

    public static string Folio(int anio, int consecutivo) => $"FOL-{anio}-{consecutivo:000}";
}

public static class Periodicidad
{
    /// <summary>Periodos de nómina por mes: la tasa de un préstamo es mensual y la cuota se cobra por periodo.</summary>
    public static decimal PeriodosPorMes(FrecuenciaNomina frecuencia) =>
        frecuencia == FrecuenciaNomina.Quincenal ? 2m : 52m / 12m;

    /// <summary>Referencia corta del periodo: Q20-2024 (quincenal) o S12-2024 (semanal).</summary>
    public static string Referencia(FrecuenciaNomina frecuencia, int numero, int anio) =>
        $"{(frecuencia == FrecuenciaNomina.Quincenal ? 'Q' : 'S')}{numero}-{anio}";

    public static string TipoPeriodo(FrecuenciaNomina frecuencia) =>
        frecuencia == FrecuenciaNomina.Quincenal ? "QUINCENAL" : "SEMANAL";
}

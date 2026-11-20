using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;

namespace CajaComun.Tests;

public class ReglasPrestamoTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(18, true)]
    [InlineData(36, true)]
    [InlineData(37, false)]
    [InlineData(-4, false)]
    public void El_plazo_esta_entre_1_y_36_cuotas(int plazo, bool esperado) =>
        Assert.Equal(esperado, ReglasPrestamo.PlazoValido(plazo));

    [Theory]
    [InlineData("2024-11-10", "2024-11-01", "2024-11-15", "2024-11-10")]   // dentro del periodo
    [InlineData("2026-11-19", "2024-11-01", "2024-11-15", "2024-11-15")]   // después: el último día
    [InlineData("2023-01-01", "2024-11-01", "2024-11-15", "2024-11-01")]   // antes: el primer día
    public void Las_operaciones_se_fechan_dentro_del_primer_periodo_abierto(string hoy, string inicio, string fin, string esperado) =>
        Assert.Equal(DateOnly.Parse(esperado), ReglasPrestamo.FechaOperacion(DateOnly.Parse(hoy), DateOnly.Parse(inicio), DateOnly.Parse(fin)));

    [Fact]
    public void El_folio_lleva_anio_y_consecutivo_de_tres_digitos()
    {
        Assert.Equal("FOL-2024-089", ReglasPrestamo.Folio(2024, 89));
        Assert.Equal("FOL-2025-001", ReglasPrestamo.Folio(2025, 1));
        Assert.Equal("FOL-2024-1000", ReglasPrestamo.Folio(2024, 1000));
    }

    [Fact]
    public void La_referencia_del_periodo_distingue_quincenas_de_semanas()
    {
        Assert.Equal("Q20-2024", Periodicidad.Referencia(FrecuenciaNomina.Quincenal, 20, 2024));
        Assert.Equal("S12-2025", Periodicidad.Referencia(FrecuenciaNomina.Semanal, 12, 2025));
        Assert.Equal("QUINCENAL", Periodicidad.TipoPeriodo(FrecuenciaNomina.Quincenal));
        Assert.Equal("SEMANAL", Periodicidad.TipoPeriodo(FrecuenciaNomina.Semanal));
    }

    [Fact]
    public void Una_caja_semanal_cobra_la_tasa_mensual_repartida_en_52_12_periodos()
    {
        Assert.Equal(2m, Periodicidad.PeriodosPorMes(FrecuenciaNomina.Quincenal));
        Assert.Equal(52m / 12m, Periodicidad.PeriodosPorMes(FrecuenciaNomina.Semanal));

        // 10,000 al 1.5% mensual en 4 semanas: interés de la primera semana = 10,000 x 1.5% / (52/12) = 34.62
        var cuotas = CalculadoraAmortizacion.Generar(10_000m, 4, 1.5m, Periodicidad.PeriodosPorMes(FrecuenciaNomina.Semanal));
        Assert.Equal(2_500m, cuotas[0].Capital);
        Assert.Equal(34.62m, cuotas[0].Interes);
        Assert.Equal(0m, cuotas[^1].SaldoFinal);
    }

    [Fact]
    public void Sin_indicar_periodicidad_la_amortizacion_sigue_siendo_quincenal()
    {
        var porDefecto = CalculadoraAmortizacion.Generar(22_500m, 18, 1.5m);
        var explicita = CalculadoraAmortizacion.Generar(22_500m, 18, 1.5m, 2m);
        Assert.Equal(explicita, porDefecto);
        Assert.Equal(1_371.88m, porDefecto[5].Total);
    }

    [Fact]
    public void La_periodicidad_debe_ser_positiva() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculadoraAmortizacion.Generar(1_000m, 4, 1.5m, 0m));
}

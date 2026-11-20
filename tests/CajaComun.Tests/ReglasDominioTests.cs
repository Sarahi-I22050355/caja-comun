using CajaComun.Domain.Reglas;

namespace CajaComun.Tests;

public class ReglasDominioTests
{
    [Fact]
    public void Amortizacion_caso_de_verificacion_cuota_6()
    {
        var tabla = CalculadoraAmortizacion.Generar(22_500m, 18, 1.5m);
        var c = tabla[5];
        Assert.Equal(6, c.Numero);
        Assert.Equal(1_250.00m, c.Capital);
        Assert.Equal(16_250.00m, c.SaldoInicial);
        Assert.Equal(121.88m, c.Interes);
        Assert.Equal(1_371.88m, c.Total);
    }

    [Theory]
    [InlineData(10_000, 18, 1.25)]
    [InlineData(7_777.77, 7, 1.75)]
    [InlineData(22_500, 18, 1.5)]
    public void Amortizacion_capital_suma_el_monto_y_termina_en_cero(decimal monto, int plazo, decimal tasa)
    {
        var tabla = CalculadoraAmortizacion.Generar(monto, plazo, tasa);
        Assert.Equal(monto, tabla.Sum(c => c.Capital));
        Assert.Equal(0m, tabla[^1].SaldoFinal);
    }

    [Fact]
    public void Reparto_mayor_residuo_suma_exactamente_el_total()
    {
        var pesos = new decimal[] { 1m, 1m, 1m };
        var r = DistribucionProporcional.Repartir(100m, pesos);
        Assert.Equal(100m, r.Sum());
        Assert.Equal(new[] { 33.34m, 33.33m, 33.33m }, r);
    }

    [Fact]
    public void Reparto_con_muchos_pesos_aleatorios_siempre_cuadra()
    {
        var rnd = new Random(7);
        for (var i = 0; i < 200; i++)
        {
            var pesos = Enumerable.Range(0, rnd.Next(1, 300)).Select(_ => (decimal)rnd.Next(1, 5_000_000)).ToArray();
            var total = rnd.Next(1, 90_000_000) / 100m;
            Assert.Equal(total, DistribucionProporcional.Repartir(total, pesos).Sum());
        }
    }

    [Fact]
    public void SaldoDias_primera_aportacion_el_15_de_enero_cuenta_351_dias()
    {
        var inicio = new DateOnly(2023, 1, 1);
        var fin = new DateOnly(2023, 12, 31);
        var r = SaldoDias.Calcular([new CambioAhorro(new DateOnly(2023, 1, 15), 100m)], [], inicio, fin);
        Assert.Equal(100m * 351, r.SaldoDias);
        Assert.Equal(351, r.DiasActivos);
    }

    [Fact]
    public void SaldoDias_baja_a_mitad_de_anio_solo_cuenta_hasta_su_liquidacion()
    {
        var inicio = new DateOnly(2023, 1, 1);
        var fin = new DateOnly(2023, 12, 31);
        var baja = new DateOnly(2023, 6, 30);
        var aportaciones = new[]
        {
            new CambioAhorro(new DateOnly(2023, 1, 15), 100m),
            new CambioAhorro(new DateOnly(2023, 6, 30), 100m)
        };
        var r = SaldoDias.Calcular(aportaciones, [new CambioAhorro(baja, 200m)], inicio, fin);
        // 100 desde el 15-ene al 30-jun (167 días) y 100 solo el 30-jun (1 día)
        Assert.Equal(100m * 167 + 100m * 1, r.SaldoDias);
        Assert.Equal(167, r.DiasActivos);
    }

    [Fact]
    public void Quincena_Q20_de_2024_termina_el_31_de_octubre()
    {
        Assert.Equal(new DateOnly(2024, 10, 31), Quincenas.Rango(2024, 20).Fin);
        Assert.Equal(new DateOnly(2024, 8, 15), Quincenas.Rango(2024, 15).Fin);
        Assert.Equal(new DateOnly(2024, 2, 29), Quincenas.Rango(2024, 4).Fin);
    }

    [Theory]
    [InlineData("00248070123456789", 6)]   // suma ponderada 44 -> (10 - 4) % 10
    [InlineData("00000000000000000", 0)]
    public void Clabe_calcula_el_digito_verificador(string primeros17, int esperado)
    {
        Assert.Equal(esperado, Clabe.DigitoVerificador(primeros17));
        Assert.True(Clabe.EsValida(Clabe.Completar(primeros17)));
    }

    [Theory]
    [InlineData("002480701234567890")]   // dígito verificador incorrecto
    [InlineData("0024807012345678")]     // 16 dígitos
    [InlineData("00248070123456789X")]
    [InlineData(null)]
    public void Clabe_rechaza_valores_invalidos(string? valor) => Assert.False(Clabe.EsValida(valor));

    [Fact]
    public void Limite_de_credito_es_80_por_ciento_del_ahorro()
    {
        Assert.Equal(38_920.00m, CapacidadCredito.Limite(48_650.00m));
        Assert.Equal(23_920.00m, CapacidadCredito.DisponibleSinAval(48_650.00m, 15_000m));
    }

    [Fact]
    public void Excedente_sobre_el_disponible_requiere_aval()
    {
        Assert.False(CapacidadCredito.RequiereAval(20_000m, 48_650m, 15_000m));
        Assert.True(CapacidadCredito.RequiereAval(25_000m, 48_650m, 15_000m));
        Assert.Equal(1_080.00m, CapacidadCredito.ExcedenteAval(25_000m, 48_650m, 15_000m));
    }

    [Fact]
    public void Redondeo_aleja_de_cero()
    {
        Assert.Equal(121.88m, Dinero.Redondear(121.875m));
    }
}

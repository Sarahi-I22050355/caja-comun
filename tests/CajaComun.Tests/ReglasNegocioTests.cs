using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;

namespace CajaComun.Tests;

public class DescuentoPeriodoTests
{
    // Axel Carlin Guzmán en Q20: 1,400.00 + 500.00 + cuota 6 del préstamo (1,371.88).
    private static readonly CuotaVigente CuotaAxel = new(1_400m, 500m, VoluntarioActivo: true);
    private static readonly decimal CuotaPrestamoQ20 = CalculadoraAmortizacion.Generar(22_500m, 18, 1.5m)[5].Total;

    [Fact]
    public void Axel_en_Q20_descuenta_3271_88_con_el_voluntario_aparte()
    {
        var d = CalculadoraDescuento.Calcular(CuotaAxel, CuotaPrestamoQ20, []);
        Assert.Equal(1_400.00m, d.Ordinaria);
        Assert.Equal(500.00m, d.Voluntaria);
        Assert.Equal(1_371.88m, d.Prestamo);
        Assert.Equal(3_271.88m, d.Total);
        Assert.False(d.Suspendido);
    }

    [Theory]
    [InlineData(TipoIncidencia.Licencia)]
    [InlineData(TipoIncidencia.Incapacidad)]
    [InlineData(TipoIncidencia.Baja)]
    public void Licencia_incapacidad_y_baja_suspenden_todo_el_descuento(TipoIncidencia tipo)
    {
        var d = CalculadoraDescuento.Calcular(CuotaAxel, CuotaPrestamoQ20, [tipo]);
        Assert.True(d.Suspendido);
        Assert.Equal(0m, d.Total);
    }

    [Fact]
    public void Suspender_el_voluntario_deja_intacto_lo_demas()
    {
        var d = CalculadoraDescuento.Calcular(CuotaAxel, CuotaPrestamoQ20, [TipoIncidencia.SuspendeVoluntario]);
        Assert.Equal(0m, d.Voluntaria);
        Assert.Equal(1_400m + 1_371.88m, d.Total);
        Assert.False(d.Suspendido);
    }

    [Fact]
    public void Un_voluntario_inactivo_no_se_descuenta_aunque_tenga_monto()
    {
        var d = CalculadoraDescuento.Calcular(new CuotaVigente(1_400m, 500m, VoluntarioActivo: false), 0m, []);
        Assert.Equal(0m, d.Voluntaria);
        Assert.Equal(1_400m, d.Total);
    }

    [Fact]
    public void Cambio_de_cuota_no_suspende_nada()
    {
        var d = CalculadoraDescuento.Calcular(CuotaAxel, 0m, [TipoIncidencia.CambioCuota]);
        Assert.Equal(1_900m, d.Total);
    }
}

public class EvaluadorGarantiaTests
{
    // Axel: ahorro 48,650 -> límite 38,920 y sin préstamos que descontar.
    [Fact]
    public void Dentro_del_limite_la_garantia_es_el_ahorro()
    {
        var r = EvaluadorGarantia.Evaluar(30_000m, 48_650m, 0m, disponibleAval: null);
        Assert.Equal(GarantiaPrestamo.Ahorro80, r.Garantia);
        Assert.True(r.Aprobada);
        Assert.Equal(0m, r.ExcedenteAval);
    }

    [Fact]
    public void El_monto_exactamente_igual_al_disponible_no_requiere_aval()
    {
        Assert.True(EvaluadorGarantia.Evaluar(38_920m, 48_650m, 0m, null).Aprobada);
        Assert.False(CapacidadCredito.RequiereAval(38_920m, 48_650m, 0m));
    }

    [Fact]
    public void Si_excede_se_exige_aval()
    {
        var r = EvaluadorGarantia.Evaluar(40_000m, 48_650m, 0m, disponibleAval: null);
        Assert.Equal(GarantiaPrestamo.PagareAval, r.Garantia);
        Assert.False(r.Aprobada);
        Assert.Equal(1_080m, r.ExcedenteAval);
        Assert.Equal(EvaluadorGarantia.AvalRequerido, r.Motivo);
    }

    [Fact]
    public void El_aval_debe_cubrir_el_excedente_completo()
    {
        Assert.Equal(EvaluadorGarantia.AvalInsuficiente, EvaluadorGarantia.Evaluar(40_000m, 48_650m, 0m, 1_079.99m).Motivo);
        var ok = EvaluadorGarantia.Evaluar(40_000m, 48_650m, 0m, 1_080m);
        Assert.True(ok.Aprobada);
        Assert.Equal(GarantiaPrestamo.PagareAval, ok.Garantia);
    }

    [Fact]
    public void Los_prestamos_vigentes_reducen_el_disponible()
    {
        // Límite 38,920 - saldo 15,000 = 23,920 disponibles.
        Assert.Equal(23_920m, CapacidadCredito.DisponibleSinAval(48_650m, 15_000m));
        Assert.True(CapacidadCredito.RequiereAval(25_000m, 48_650m, 15_000m));
        Assert.Equal(1_080m, CapacidadCredito.ExcedenteAval(25_000m, 48_650m, 15_000m));
    }

    [Fact]
    public void Lo_que_ya_se_avala_no_se_puede_comprometer_otra_vez()
    {
        Assert.Equal(7_000m, EvaluadorGarantia.DisponibleComoAval(20_000m, 5_000m, 4_000m)); // 16,000 - 5,000 - 4,000
        Assert.Equal(0m, EvaluadorGarantia.DisponibleComoAval(1_000m, 0m, 5_000m));
    }
}

public class PermisosTests
{
    [Theory]
    [InlineData(RolUsuario.Administrador, AccionSistema.Leer, true)]
    [InlineData(RolUsuario.Administrador, AccionSistema.Capturar, true)]
    [InlineData(RolUsuario.Administrador, AccionSistema.Administrar, true)]
    [InlineData(RolUsuario.Capturista, AccionSistema.Leer, true)]
    [InlineData(RolUsuario.Capturista, AccionSistema.Capturar, true)]
    [InlineData(RolUsuario.Capturista, AccionSistema.Administrar, false)]
    [InlineData(RolUsuario.Consulta, AccionSistema.Leer, true)]
    [InlineData(RolUsuario.Consulta, AccionSistema.Capturar, false)]
    [InlineData(RolUsuario.Consulta, AccionSistema.Administrar, false)]
    public void Matriz_de_permisos_por_rol(RolUsuario rol, AccionSistema accion, bool esperado) =>
        Assert.Equal(esperado, Permisos.Puede(rol, accion));

    [Fact]
    public void Una_caja_se_ve_con_acceso_global_o_asignada()
    {
        Assert.True(Permisos.PuedeVerCaja(true, [], 9));
        Assert.True(Permisos.PuedeVerCaja(false, [1, 2], 2));
        Assert.False(Permisos.PuedeVerCaja(false, [1, 2], 3));
        Assert.False(Permisos.PuedeVerCaja(false, [], 1));
    }
}

public class PeriodosCicloTests
{
    [Fact]
    public void Quincenal_da_24_periodos_contiguos_y_Q20_termina_el_31_de_octubre()
    {
        var p = PeriodosCiclo.Generar(FrecuenciaNomina.Quincenal, 2024);
        Assert.Equal(24, p.Count);
        Assert.Equal(new DateOnly(2024, 1, 1), p[0].Inicio);
        Assert.Equal(new DateOnly(2024, 12, 31), p[^1].Fin);
        Assert.Equal(new DateOnly(2024, 10, 31), p[19].Fin);
        Assert.Equal(new DateOnly(2024, 2, 29), p[3].Fin); // 2024 es bisiesto
        SinHuecos(p);
    }

    [Theory]
    [InlineData(2023)]
    [InlineData(2024)]
    [InlineData(2025)]
    public void Semanal_da_52_periodos_de_7_dias_y_el_ultimo_cierra_el_anio(int anio)
    {
        var p = PeriodosCiclo.Generar(FrecuenciaNomina.Semanal, anio);
        Assert.Equal(52, p.Count);
        Assert.Equal(new DateOnly(anio, 1, 1), p[0].Inicio);
        Assert.Equal(new DateOnly(anio, 12, 31), p[^1].Fin);
        Assert.All(p.Take(51), x => Assert.Equal(7, x.Fin.DayNumber - x.Inicio.DayNumber + 1));
        SinHuecos(p);
    }

    private static void SinHuecos(IReadOnlyList<RangoPeriodo> periodos)
    {
        for (var i = 1; i < periodos.Count; i++)
            Assert.Equal(periodos[i - 1].Fin.AddDays(1), periodos[i].Inicio);
        Assert.Equal(Enumerable.Range(1, periodos.Count), periodos.Select(x => x.Numero));
    }
}

public class ReglasCajaYAhorradorTests
{
    [Theory]
    [InlineData(1.00, true)]
    [InlineData(1.25, true)]
    [InlineData(1.5, true)]
    [InlineData(2.00, true)]
    [InlineData(0.99, false)]
    [InlineData(2.01, false)]
    [InlineData(1.255, false)]
    public void La_tasa_mensual_va_de_1_a_2_con_dos_decimales(double tasa, bool esperado) =>
        Assert.Equal(esperado, ReglasCaja.TasaValida((decimal)tasa));

    [Theory]
    [InlineData("CAGJ850101AB1", true)]
    [InlineData("CAGJ850101", false)]
    [InlineData("cagj850101ab1", false)]
    [InlineData("1AGJ850101AB1", false)]
    [InlineData(null, false)]
    public void Formato_de_rfc(string? rfc, bool esperado) => Assert.Equal(esperado, ReglasAhorrador.RfcValido(rfc));

    [Fact]
    public void Numero_de_empleado_lleva_prefijo_y_cinco_digitos() =>
        Assert.Equal("GIM-00123", ReglasAhorrador.NumeroEmpleado("GIM", 123));
}

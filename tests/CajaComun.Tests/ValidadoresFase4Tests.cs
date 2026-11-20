using CajaComun.Application.AhorroVoluntario;
using CajaComun.Application.Nomina;
using CajaComun.Application.Prestamos;
using CajaComun.Domain.Enums;

namespace CajaComun.Tests;

public class ValidadoresFase4Tests
{
    private static string[] Codigos(FluentValidation.Results.ValidationResult r, string campo) =>
        r.Errors.Where(e => e.PropertyName == campo).Select(e => e.ErrorCode).ToArray();

    // ---------- Incidencias ----------

    [Theory]
    [InlineData(TipoIncidencia.Licencia)]
    [InlineData(TipoIncidencia.Incapacidad)]
    [InlineData(TipoIncidencia.SuspendeVoluntario)]
    public void Las_incidencias_simples_no_piden_valor_ni_fecha(TipoIncidencia tipo) =>
        Assert.True(new CrearIncidenciaRequestValidator().Validate(new CrearIncidenciaRequest(1, 1, tipo, null, null, null)).IsValid);

    [Fact]
    public void El_cambio_de_cuota_pide_un_importe_no_negativo_con_dos_decimales()
    {
        var v = new CrearIncidenciaRequestValidator();
        Assert.Contains("REQUERIDO", Codigos(v.Validate(new CrearIncidenciaRequest(1, 1, TipoIncidencia.CambioCuota, null, null, null)), "Valor"));
        Assert.Contains("MONTO_NEGATIVO", Codigos(v.Validate(new CrearIncidenciaRequest(1, 1, TipoIncidencia.CambioCuota, -1m, null, null)), "Valor"));
        Assert.Contains("MONTO_INVALIDO", Codigos(v.Validate(new CrearIncidenciaRequest(1, 1, TipoIncidencia.CambioCuota, 10.123m, null, null)), "Valor"));
        Assert.True(v.Validate(new CrearIncidenciaRequest(1, 1, TipoIncidencia.CambioCuota, 777m, null, null)).IsValid);
    }

    [Fact]
    public void La_baja_pide_fecha_y_las_referencias_deben_existir()
    {
        var v = new CrearIncidenciaRequestValidator();
        Assert.Contains("REQUERIDO", Codigos(v.Validate(new CrearIncidenciaRequest(1, 1, TipoIncidencia.Baja, null, null, null)), "FechaBaja"));
        Assert.True(v.Validate(new CrearIncidenciaRequest(1, 1, TipoIncidencia.Baja, null, null, new DateOnly(2024, 11, 5))).IsValid);
        Assert.Contains("REQUERIDO", Codigos(v.Validate(new CrearIncidenciaRequest(0, 1, TipoIncidencia.Licencia, null, null, null)), "AhorradorId"));
        Assert.Contains("LONGITUD_MAXIMA", Codigos(v.Validate(new CrearIncidenciaRequest(1, 1, TipoIncidencia.Licencia, null, new string('x', 501), null)), "Nota"));
    }

    // ---------- Préstamos ----------

    [Fact]
    public void Una_solicitud_valida_no_tiene_errores() =>
        Assert.True(new SolicitudPrestamoRequestValidator().Validate(new SolicitudPrestamoRequest(1, 22_500m, 18, null)).IsValid);

    [Theory]
    [InlineData(0, 18, "Monto")]
    [InlineData(-5, 18, "Monto")]
    [InlineData(1000.123, 18, "Monto")]
    [InlineData(1000, 0, "PlazoQuincenas")]
    [InlineData(1000, 37, "PlazoQuincenas")]
    public void Monto_y_plazo_fuera_de_rango_se_rechazan(double monto, int plazo, string campo)
    {
        var r = new SolicitudPrestamoRequestValidator().Validate(new SolicitudPrestamoRequest(1, (decimal)monto, plazo, null));
        Assert.NotEmpty(Codigos(r, campo));
    }

    [Fact]
    public void El_aval_si_se_indica_debe_ser_un_identificador_valido()
    {
        var v = new SolicitudPrestamoRequestValidator();
        Assert.Contains("VALOR_INVALIDO", Codigos(v.Validate(new SolicitudPrestamoRequest(1, 1000m, 6, 0)), "AvalAhorradorId"));
        Assert.True(v.Validate(new SolicitudPrestamoRequest(1, 1000m, 6, 5)).IsValid);
    }

    // ---------- Ahorro voluntario ----------

    [Fact]
    public void Activar_el_voluntario_exige_un_importe_mayor_a_cero_y_desactivarlo_lo_deja_en_cero()
    {
        var v = new ActualizarVoluntarioRequestValidator();
        Assert.True(v.Validate(new ActualizarVoluntarioRequest(true, 300m, null)).IsValid);
        Assert.True(v.Validate(new ActualizarVoluntarioRequest(false, 0m, null)).IsValid);
        Assert.Contains("MONTO_INVALIDO", Codigos(v.Validate(new ActualizarVoluntarioRequest(true, 0m, null)), "CuotaVoluntaria"));
        Assert.Contains("VOLUNTARIO_INACTIVO", Codigos(v.Validate(new ActualizarVoluntarioRequest(false, 100m, null)), "CuotaVoluntaria"));
        Assert.Contains("MONTO_NEGATIVO", Codigos(v.Validate(new ActualizarVoluntarioRequest(true, -1m, null)), "CuotaVoluntaria"));
    }
}

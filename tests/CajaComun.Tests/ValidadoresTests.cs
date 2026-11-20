using CajaComun.Application.Ahorradores;
using CajaComun.Application.Cajas;
using CajaComun.Application.Usuarios;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;

namespace CajaComun.Tests;

public class ValidadoresTests
{
    private static CrearAhorradorRequest AltaValida() => new(
        CajaId: 1, NumEmpleado: null, Nombre: "Jesús", Apellidos: "Carreón Moncada", Rfc: "CAMJ850101AB1",
        FechaNacimiento: new DateOnly(1985, 1, 1), FechaIngreso: new DateOnly(2024, 3, 4), SueldoQuincenal: 9_800m,
        Clabe: Clabe.Completar("00248070123456789"), Departamento: "Producción",
        CuotaOrdinaria: 900m, CuotaVoluntaria: 0m, VoluntarioActivo: false);

    private static string[] Codigos(FluentValidation.Results.ValidationResult r, string campo) =>
        r.Errors.Where(e => e.PropertyName == campo).Select(e => e.ErrorCode).ToArray();

    [Fact]
    public void Alta_valida_no_tiene_errores() =>
        Assert.True(new CrearAhorradorRequestValidator().Validate(AltaValida()).IsValid);

    [Fact]
    public void Alta_con_datos_malos_devuelve_codigos_estables_por_campo()
    {
        var mala = AltaValida() with
        {
            Rfc = "XX", Clabe = "002480701234567890", Nombre = "", SueldoQuincenal = -1m, NumEmpleado = "123"
        };
        var r = new CrearAhorradorRequestValidator().Validate(mala);
        Assert.Contains("RFC_INVALIDO", Codigos(r, "Rfc"));
        Assert.Contains("CLABE_INVALIDA", Codigos(r, "Clabe"));
        Assert.Contains("REQUERIDO", Codigos(r, "Nombre"));
        Assert.Contains("MONTO_NEGATIVO", Codigos(r, "SueldoQuincenal"));
        Assert.Contains("NUM_EMPLEADO_INVALIDO", Codigos(r, "NumEmpleado"));
    }

    [Fact]
    public void El_voluntario_con_monto_exige_estar_activo()
    {
        var r = new CrearAhorradorRequestValidator().Validate(AltaValida() with { CuotaVoluntaria = 500m, VoluntarioActivo = false });
        Assert.Contains("VOLUNTARIO_INACTIVO", Codigos(r, "CuotaVoluntaria"));
    }

    [Fact]
    public void Los_importes_no_admiten_mas_de_dos_decimales()
    {
        var r = new CrearCuotaRequestValidator().Validate(new CrearCuotaRequest(100.555m, 0m, false, new DateOnly(2024, 11, 1)));
        Assert.Contains("MONTO_INVALIDO", Codigos(r, "CuotaOrdinaria"));
    }

    [Fact]
    public void Caja_exige_empresa_existente_o_nueva_pero_no_ambas()
    {
        var v = new CrearCajaRequestValidator();
        var baseReq = new CrearCajaRequest(1, null, "Caja Norte", "NOR", 1.5m, FrecuenciaNomina.Quincenal, 2025);
        Assert.True(v.Validate(baseReq).IsValid);
        Assert.True(v.Validate(baseReq with { EmpresaId = null, EmpresaNueva = "Norte SA" }).IsValid);
        Assert.False(v.Validate(baseReq with { EmpresaId = null }).IsValid);
        Assert.False(v.Validate(baseReq with { EmpresaNueva = "Norte SA" }).IsValid);
    }

    [Fact]
    public void Caja_rechaza_tasa_fuera_de_rango_y_prefijo_malo()
    {
        var r = new CrearCajaRequestValidator().Validate(
            new CrearCajaRequest(1, null, "Caja", "n1", 2.5m, FrecuenciaNomina.Quincenal, 2025));
        Assert.Contains("TASA_FUERA_DE_RANGO", Codigos(r, "TasaInteresMensual"));
        Assert.Contains("PREFIJO_INVALIDO", Codigos(r, "Prefijo"));
    }

    [Theory]
    [InlineData("Corta1a", false)]
    [InlineData("sinmayusculas123", false)]
    [InlineData("SINMINUSCULAS123", false)]
    [InlineData("SinNumerosAqui", false)]
    [InlineData("Segura2024Caja", true)]
    [InlineData(null, false)]
    public void Politica_de_contrasenas(string? password, bool esperado) =>
        Assert.Equal(esperado, PoliticaPassword.Cumple(password));

    [Fact]
    public void Paginacion_se_acota_al_maximo_y_al_minimo()
    {
        var c = new ConsultaAhorradores { Page = 0, PageSize = 100_000 };
        Assert.Equal(1, c.PaginaSegura);
        Assert.Equal(100, c.TamanoSeguro);
        Assert.Equal(0, c.Saltar);
        Assert.Equal(25, (c with { Page = 2, PageSize = 25 }).Saltar);
    }
}

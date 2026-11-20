using CajaComun.Application.Comun;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;
using FluentValidation;

namespace CajaComun.Application.Ahorradores;

public sealed record AhorradorListaDto(
    int Id, int CajaId, string Caja, string NumEmpleado, string Nombre, string Apellidos, string Departamento,
    EstadoAhorrador Estado, decimal SueldoQuincenal, decimal AhorroTotal);

public sealed record CuotaDto(
    int Id, decimal CuotaOrdinaria, decimal CuotaVoluntaria, bool VoluntarioActivo, DateOnly VigenteDesde);

public sealed record AhorroResumenDto(
    decimal Ordinario, decimal Voluntario, decimal Total, decimal LimitePrestamo, decimal SaldoPrestamos,
    decimal DisponibleSinAval);

public sealed record PrestamoVigenteDto(
    string Folio, decimal Monto, int PlazoQuincenas, decimal TasaMensual, decimal SaldoVigente, int CuotasRestantes);

public sealed record AhorradorDetalleDto(
    int Id, int CajaId, string Caja, string NumEmpleado, string Nombre, string Apellidos, string Rfc,
    DateOnly FechaNacimiento, DateOnly FechaIngreso, DateOnly? FechaBaja, EstadoAhorrador Estado,
    decimal SueldoQuincenal, string Clabe, string Departamento, CuotaDto? CuotaVigente,
    AhorroResumenDto Ahorro, IReadOnlyList<PrestamoVigenteDto> PrestamosVigentes);

public sealed record ConsultaAhorradores : ConsultaPaginada
{
    public int? CajaId { get; init; }
    public EstadoAhorrador? Estado { get; init; }
    /// <summary>Texto libre sobre nombre, apellidos o número de empleado.</summary>
    public string? Q { get; init; }
    /// <summary>numEmpleado | nombre | ahorro | ingreso (por defecto nombre).</summary>
    public string? Orden { get; init; }
    public bool Desc { get; init; }
}

/// <summary>Si no se manda NumEmpleado se asigna el siguiente consecutivo de la caja.</summary>
public sealed record CrearAhorradorRequest(
    int CajaId, string? NumEmpleado, string Nombre, string Apellidos, string Rfc, DateOnly FechaNacimiento,
    DateOnly FechaIngreso, decimal SueldoQuincenal, string Clabe, string Departamento,
    decimal CuotaOrdinaria, decimal CuotaVoluntaria, bool VoluntarioActivo);

/// <summary>Datos personales y de nómina. El estado y la baja se manejan desde incidencias.</summary>
public sealed record ActualizarAhorradorRequest(
    string Nombre, string Apellidos, string Rfc, DateOnly FechaNacimiento, decimal SueldoQuincenal,
    string Clabe, string Departamento);

public sealed record CrearCuotaRequest(
    decimal CuotaOrdinaria, decimal CuotaVoluntaria, bool VoluntarioActivo, DateOnly VigenteDesde);

public sealed record ConsultaMovimientos : ConsultaPaginada
{
    public TipoMovimiento? Tipo { get; init; }
    public DateOnly? Desde { get; init; }
    public DateOnly? Hasta { get; init; }
    public int? PeriodoId { get; init; }
}

public sealed record MovimientoDto(
    long Id, DateOnly Fecha, TipoMovimiento Tipo, decimal Monto, string? Referencia, int? PeriodoId,
    string? FolioPrestamo, decimal EfectoAhorro, decimal SaldoAhorro);

public sealed record EstadoCuentaTotalesDto(
    decimal AportacionesOrdinarias, decimal AportacionesVoluntarias, decimal Liquidaciones, decimal Desembolsos,
    decimal AbonosCapital, decimal AbonosInteres, decimal RendimientoAnual);

public sealed record EstadoCuentaDto(
    int AhorradorId, string NumEmpleado, string Nombre, string Caja, int Anio, decimal SaldoInicial,
    decimal SaldoFinal, EstadoCuentaTotalesDto Totales, IReadOnlyList<MovimientoDto> Movimientos);

public interface IAhorradorService
{
    Task<Pagina<AhorradorListaDto>> ListarAsync(ConsultaAhorradores consulta, CancellationToken ct = default);
    Task<AhorradorDetalleDto> ObtenerAsync(int id, CancellationToken ct = default);
    Task<AhorradorDetalleDto> CrearAsync(CrearAhorradorRequest request, CancellationToken ct = default);
    Task<AhorradorDetalleDto> ActualizarAsync(int id, ActualizarAhorradorRequest request, CancellationToken ct = default);
    Task<Pagina<MovimientoDto>> ListarMovimientosAsync(int id, ConsultaMovimientos consulta, CancellationToken ct = default);
    Task<EstadoCuentaDto> EstadoCuentaAsync(int id, int anio, CancellationToken ct = default);
    Task<IReadOnlyList<CuotaDto>> ListarCuotasAsync(int id, CancellationToken ct = default);
    Task<CuotaDto> CrearCuotaAsync(int id, CrearCuotaRequest request, CancellationToken ct = default);
}

public sealed class CrearAhorradorRequestValidator : AbstractValidator<CrearAhorradorRequest>
{
    public CrearAhorradorRequestValidator()
    {
        RuleFor(x => x.CajaId).GreaterThan(0).WithErrorCode("REQUERIDO");
        RuleFor(x => x.NumEmpleado).Matches("^[A-Z]{2,5}-[0-9]{5}$").WithErrorCode("NUM_EMPLEADO_INVALIDO")
            .When(x => !string.IsNullOrWhiteSpace(x.NumEmpleado));
        Comunes.Persona(this, x => x.Nombre, x => x.Apellidos, x => x.Rfc, x => x.FechaNacimiento,
            x => x.SueldoQuincenal, x => x.Clabe, x => x.Departamento);
        RuleFor(x => x.FechaIngreso).GreaterThan(x => x.FechaNacimiento).WithErrorCode("FECHA_INVALIDA");
        RuleFor(x => x.CuotaOrdinaria).GreaterThanOrEqualTo(0m).WithErrorCode("MONTO_NEGATIVO").PrecisionScale(14, 2, true).WithErrorCode("MONTO_INVALIDO");
        RuleFor(x => x.CuotaVoluntaria).GreaterThanOrEqualTo(0m).WithErrorCode("MONTO_NEGATIVO").PrecisionScale(14, 2, true).WithErrorCode("MONTO_INVALIDO");
        RuleFor(x => x.CuotaVoluntaria).Equal(0m).When(x => !x.VoluntarioActivo).WithErrorCode("VOLUNTARIO_INACTIVO");
    }
}

public sealed class ActualizarAhorradorRequestValidator : AbstractValidator<ActualizarAhorradorRequest>
{
    public ActualizarAhorradorRequestValidator() =>
        Comunes.Persona(this, x => x.Nombre, x => x.Apellidos, x => x.Rfc, x => x.FechaNacimiento,
            x => x.SueldoQuincenal, x => x.Clabe, x => x.Departamento);
}

public sealed class CrearCuotaRequestValidator : AbstractValidator<CrearCuotaRequest>
{
    public CrearCuotaRequestValidator()
    {
        RuleFor(x => x.CuotaOrdinaria).GreaterThanOrEqualTo(0m).WithErrorCode("MONTO_NEGATIVO").PrecisionScale(14, 2, true).WithErrorCode("MONTO_INVALIDO");
        RuleFor(x => x.CuotaVoluntaria).GreaterThanOrEqualTo(0m).WithErrorCode("MONTO_NEGATIVO").PrecisionScale(14, 2, true).WithErrorCode("MONTO_INVALIDO");
        RuleFor(x => x.CuotaVoluntaria).Equal(0m).When(x => !x.VoluntarioActivo).WithErrorCode("VOLUNTARIO_INACTIVO");
    }
}

internal static class Comunes
{
    /// <summary>Reglas compartidas por el alta y la edición de un ahorrador.</summary>
    public static void Persona<T>(
        AbstractValidator<T> v,
        System.Linq.Expressions.Expression<Func<T, string>> nombre,
        System.Linq.Expressions.Expression<Func<T, string>> apellidos,
        System.Linq.Expressions.Expression<Func<T, string>> rfc,
        System.Linq.Expressions.Expression<Func<T, DateOnly>> nacimiento,
        System.Linq.Expressions.Expression<Func<T, decimal>> sueldo,
        System.Linq.Expressions.Expression<Func<T, string>> clabe,
        System.Linq.Expressions.Expression<Func<T, string>> departamento)
    {
        v.RuleFor(nombre).NotEmpty().WithErrorCode("REQUERIDO").MaximumLength(100).WithErrorCode("LONGITUD_MAXIMA");
        v.RuleFor(apellidos).NotEmpty().WithErrorCode("REQUERIDO").MaximumLength(100).WithErrorCode("LONGITUD_MAXIMA");
        v.RuleFor(rfc).Must(ReglasAhorrador.RfcValido).WithErrorCode("RFC_INVALIDO");
        v.RuleFor(nacimiento).Must(f => f < DateOnly.FromDateTime(DateTime.UtcNow) && f.Year >= 1900).WithErrorCode("FECHA_INVALIDA");
        v.RuleFor(sueldo).GreaterThanOrEqualTo(0m).WithErrorCode("MONTO_NEGATIVO").PrecisionScale(14, 2, true).WithErrorCode("MONTO_INVALIDO");
        v.RuleFor(clabe).Must(Clabe.EsValida).WithErrorCode("CLABE_INVALIDA");
        v.RuleFor(departamento).NotEmpty().WithErrorCode("REQUERIDO").MaximumLength(100).WithErrorCode("LONGITUD_MAXIMA");
    }
}

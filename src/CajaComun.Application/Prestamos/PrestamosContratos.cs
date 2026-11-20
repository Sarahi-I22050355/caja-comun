using CajaComun.Application.Comun;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;
using FluentValidation;

namespace CajaComun.Application.Prestamos;

/// <summary>Monto y plazo (en periodos de nómina) con el aval opcional, que solo cuenta si el ahorro no alcanza.</summary>
public sealed record SolicitudPrestamoRequest(int AhorradorId, decimal Monto, int PlazoQuincenas, int? AvalAhorradorId);

/// <summary>Aval propuesto en una evaluación, con lo que puede respaldar todavía.</summary>
public sealed record AvalDto(int AhorradorId, string NumEmpleado, string Nombre, decimal Disponible);

public sealed record AvalResumenDto(int AhorradorId, string NumEmpleado, string Nombre);

public sealed record EvaluacionPrestamoDto(
    decimal Ahorro, decimal Limite, decimal SaldoPrestamos, decimal DisponibleSinAval, bool RequiereAval,
    decimal ExcedenteAval, GarantiaPrestamo Garantia, bool Aprobada, string? Motivo, AvalDto? Aval);

public sealed record CuotaPrestamoDto(
    int Numero, int? PeriodoId, string? Periodo, DateOnly? Fecha, decimal SaldoInicial, decimal Capital, decimal Interes,
    decimal Total, decimal SaldoFinal, EstadoAmortizacion? Estado);

public sealed record SimulacionPrestamoDto(
    int CajaId, FrecuenciaNomina Frecuencia, decimal TasaMensual, int PlazoQuincenas, EvaluacionPrestamoDto Evaluacion,
    decimal PrimeraCuota, decimal TotalIntereses, decimal TotalAPagar, IReadOnlyList<CuotaPrestamoDto> Amortizacion);

public sealed record PrestamoDto(
    int Id, string Folio, int CajaId, string Caja, int AhorradorId, string NumEmpleado, string Ahorrador, decimal Monto,
    int PlazoQuincenas, decimal TasaMensual, GarantiaPrestamo Garantia, AvalResumenDto? Aval, decimal ExcedenteAval,
    EstadoPrestamo Estado, DateOnly FechaSolicitud, DateOnly? FechaAutorizacion, string? AutorizadoPor,
    decimal CapitalPagado, decimal InteresPagado, decimal SaldoVigente, int CuotasAplicadas, int CuotasRestantes);

public sealed record PrestamoDetalleDto(
    PrestamoDto Prestamo, IReadOnlyList<CuotaPrestamoDto> Amortizacion, int? CuotaActual);

public sealed record CarteraResumenDto(
    int Vigentes, int Pendientes, int Liquidados, int Rechazados, decimal CapitalColocado, decimal SaldoVigente,
    decimal InteresCobrado);

public sealed record ConsultaPrestamos : ConsultaPaginada
{
    public int? CajaId { get; init; }
    public int? AhorradorId { get; init; }
    public EstadoPrestamo? Estado { get; init; }
    /// <summary>Folio, número de empleado o nombre.</summary>
    public string? Q { get; init; }
    /// <summary>folio (defecto, el más reciente primero), monto, saldo o solicitud.</summary>
    public string? Orden { get; init; }
    public bool Desc { get; init; } = true;
}

public interface IPrestamoService
{
    Task<SimulacionPrestamoDto> SimularAsync(SolicitudPrestamoRequest request, CancellationToken ct = default);
    Task<PrestamoDetalleDto> CrearAsync(SolicitudPrestamoRequest request, CancellationToken ct = default);
    Task<Pagina<PrestamoDto>> ListarAsync(ConsultaPrestamos consulta, CancellationToken ct = default);
    Task<CarteraResumenDto> ResumenAsync(int? cajaId, CancellationToken ct = default);
    Task<PrestamoDetalleDto> ObtenerAsync(int id, CancellationToken ct = default);
    Task<PrestamoDetalleDto> AutorizarAsync(int id, CancellationToken ct = default);
    Task<PrestamoDetalleDto> RechazarAsync(int id, CancellationToken ct = default);
}

public sealed class SolicitudPrestamoRequestValidator : AbstractValidator<SolicitudPrestamoRequest>
{
    public SolicitudPrestamoRequestValidator()
    {
        RuleFor(x => x.AhorradorId).GreaterThan(0).WithErrorCode("REQUERIDO");
        RuleFor(x => x.Monto).GreaterThan(0m).WithErrorCode("MONTO_INVALIDO")
            .PrecisionScale(14, 2, true).WithErrorCode("MONTO_INVALIDO");
        RuleFor(x => x.PlazoQuincenas).Must(ReglasPrestamo.PlazoValido).WithErrorCode("PLAZO_INVALIDO");
        RuleFor(x => x.AvalAhorradorId).GreaterThan(0).WithErrorCode("VALOR_INVALIDO").When(x => x.AvalAhorradorId is not null);
    }
}

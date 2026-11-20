using CajaComun.Application.Movimientos;
using CajaComun.Application.Nomina;
using CajaComun.Domain.Enums;
using FluentValidation;

namespace CajaComun.Application.Reportes;

public enum FormatoReporte { Json, Pdf, Xlsx }

public enum TipoReporte { EstadoCuenta, EstadoResultados, Distribucion }

public sealed record ResultadoMesDto(int Mes, decimal Cobrados, decimal Devengados);

/// <summary>
/// Utilidad a distribuir = intereses cobrados del ejercicio: la caja no tiene gastos registrados, así que no se
/// descuenta nada. El interés devengado es el calculado por la tarea programada sobre el saldo insoluto.
/// </summary>
public sealed record EstadoResultadosDto(
    int CajaId, string Caja, int Anio, EstadoCiclo? EstadoCiclo, decimal InteresesCobrados, decimal InteresesDevengados,
    decimal UtilidadADistribuir, decimal InteresDistribuido, decimal SaldoCartera, int PrestamosVigentes,
    IReadOnlyList<ResultadoMesDto> Meses);

public sealed record ReporteTrabajoRequest(
    TipoReporte Tipo, FormatoReporte Formato, int? AhorradorId, int? CajaId, int? CicloId, int? Anio);

public interface IReporteService
{
    Task<EstadoResultadosDto> EstadoResultadosAsync(int cajaId, int anio, CancellationToken ct = default);
    Task<ArchivoGenerado> EstadoResultadosArchivoAsync(int cajaId, int anio, FormatoReporte formato, CancellationToken ct = default);
    Task<ArchivoGenerado> EstadoCuentaArchivoAsync(int ahorradorId, int anio, FormatoReporte formato, CancellationToken ct = default);
    Task<ArchivoGenerado> DistribucionArchivoAsync(int cicloId, FormatoReporte formato, CancellationToken ct = default);
    Task<ArchivoGenerado> GenerarAsync(ReporteTrabajoRequest request, CancellationToken ct = default);
}

public sealed record PuntoMesDto(int Anio, int Mes, decimal AhorroAportado, decimal PrestamosDesembolsados, decimal CapitalCobrado);

public sealed record DashboardDto(
    decimal FondoTotal, int AhorradoresActivos, int PrestamosVigentes, decimal SaldoPrestamos, int AnioCiclo,
    decimal InteresAcumuladoCiclo, IReadOnlyList<PuntoMesDto> Serie, IReadOnlyList<MovimientoCajaDto> UltimosMovimientos);

public interface IDashboardService
{
    Task<DashboardDto> ObtenerAsync(int? cajaId, CancellationToken ct = default);
}

public sealed class ReporteTrabajoRequestValidator : AbstractValidator<ReporteTrabajoRequest>
{
    public ReporteTrabajoRequestValidator()
    {
        RuleFor(x => x.Tipo).IsInEnum().WithErrorCode("VALOR_INVALIDO");
        RuleFor(x => x.Formato).IsInEnum().WithErrorCode("VALOR_INVALIDO");
        RuleFor(x => x.Anio).InclusiveBetween(2000, 2100).WithErrorCode("ANIO_INVALIDO").When(x => x.Anio is not null);
        RuleFor(x => x.AhorradorId).NotNull().WithErrorCode("REQUERIDO").When(x => x.Tipo == TipoReporte.EstadoCuenta);
        RuleFor(x => x.CajaId).NotNull().WithErrorCode("REQUERIDO").When(x => x.Tipo == TipoReporte.EstadoResultados);
        RuleFor(x => x.CicloId).NotNull().WithErrorCode("REQUERIDO").When(x => x.Tipo == TipoReporte.Distribucion);
    }
}

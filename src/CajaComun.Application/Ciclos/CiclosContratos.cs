using CajaComun.Application.Comun;
using CajaComun.Application.Nomina;
using CajaComun.Domain.Enums;
using FluentValidation;

namespace CajaComun.Application.Ciclos;

public sealed record CicloDto(
    int Id, int CajaId, string Caja, int Anio, EstadoCiclo Estado, decimal InteresTotal, DateTime? FechaCierre, int Participantes);

/// <summary>Los tres pasos del cierre: interés generado, distribución y layout de dispersión.</summary>
public sealed record CicloResumenDto(
    CicloDto Ciclo, decimal InteresCobrado, decimal InteresDevengado, int PeriodosTotales, int PeriodosAplicados,
    int PrestamosVigentes, decimal SaldoDiasTotal, decimal InteresDistribuido, decimal AhorroLiquidado, decimal AhorroRetenido,
    bool PuedeCerrar, IReadOnlyList<string> Impedimentos);

public sealed record DistribucionDto(
    int AhorradorId, string NumEmpleado, string Nombre, decimal SaldoDias, decimal SaldoPromedio, int DiasActivos,
    decimal Participacion, decimal InteresAsignado, decimal AhorroLiquidado, decimal AhorroRetenido);

public sealed record ConsultaDistribucion : ConsultaPaginada
{
    public string? Q { get; init; }
}

public sealed record BorradorCicloRequest(int CajaId, int Anio);

public sealed record CierreCicloResultadoDto(
    int CicloId, int Anio, decimal InteresTotal, int Participantes, decimal AhorroLiquidado, decimal AhorroRetenido,
    int MovimientosGenerados);

public enum FormatoSpei { GenericoCsv, BanorteSimplificado }

public sealed record DispersionSpei(int AhorradorId, string NumEmpleado, string Nombre, string Clabe, decimal Importe);

/// <summary>Un layout bancario convierte las dispersiones del ciclo en el archivo que se entrega al banco.</summary>
public interface ILayoutBancario
{
    FormatoSpei Formato { get; }

    /// <summary>Valida cada renglón y arma el archivo; si alguno es inválido lanza ErrorNegocio con el detalle.</summary>
    ArchivoGenerado Generar(
        IReadOnlyList<DispersionSpei> dispersiones, int anio, DateOnly fechaAplicacion, bool incluirAhorro, string claveCaja);
}

public interface ICicloService
{
    Task<IReadOnlyList<CicloDto>> ListarAsync(int? cajaId, CancellationToken ct = default);
    Task<CicloResumenDto> ResumenAsync(int id, CancellationToken ct = default);
    Task<Pagina<DistribucionDto>> DistribucionAsync(int id, ConsultaDistribucion consulta, CancellationToken ct = default);
    /// <summary>Todos los renglones de la distribución (para reportes y archivos), sin paginar.</summary>
    Task<IReadOnlyList<DistribucionDto>> DistribucionCompletaAsync(int id, CancellationToken ct = default);
    /// <summary>Calcula (o recalcula) la distribución provisional; mientras el ciclo no se cierre se puede repetir.</summary>
    Task<CicloResumenDto> BorradorAsync(BorradorCicloRequest request, CancellationToken ct = default);
    Task<CierreCicloResultadoDto> CerrarAsync(int id, CancellationToken ct = default);
    Task<ArchivoGenerado> GenerarSpeiAsync(int id, FormatoSpei formato, bool incluirAhorro, CancellationToken ct = default);
    Task<Pagina<LayoutDto>> ListarLayoutsAsync(int id, ConsultaPaginada consulta, CancellationToken ct = default);
}

public sealed class BorradorCicloRequestValidator : AbstractValidator<BorradorCicloRequest>
{
    public BorradorCicloRequestValidator()
    {
        RuleFor(x => x.CajaId).GreaterThan(0).WithErrorCode("REQUERIDO");
        RuleFor(x => x.Anio).InclusiveBetween(2000, 2100).WithErrorCode("ANIO_INVALIDO");
    }
}

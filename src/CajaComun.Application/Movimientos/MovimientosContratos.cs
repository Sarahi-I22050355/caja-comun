using CajaComun.Application.Comun;
using CajaComun.Domain.Enums;

namespace CajaComun.Application.Movimientos;

public sealed record ConsultaMovimientosCaja : ConsultaPaginada
{
    public int? CajaId { get; init; }
    public int? AhorradorId { get; init; }
    public TipoMovimiento? Tipo { get; init; }
    public DateOnly? Desde { get; init; }
    public DateOnly? Hasta { get; init; }
    public int? PeriodoId { get; init; }
}

public sealed record MovimientoCajaDto(
    long Id, int CajaId, string Caja, int AhorradorId, string NumEmpleado, string Ahorrador, DateOnly Fecha,
    TipoMovimiento Tipo, decimal Monto, string? Referencia, int? PeriodoId, string? FolioPrestamo);

public interface IMovimientoService
{
    /// <summary>Movimientos de las cajas a las que el usuario tiene acceso, del más reciente al más antiguo.</summary>
    Task<Pagina<MovimientoCajaDto>> ListarAsync(ConsultaMovimientosCaja consulta, CancellationToken ct = default);
}

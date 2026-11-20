using CajaComun.Application.Cajas;
using CajaComun.Application.Comun;
using CajaComun.Domain.Enums;
using FluentValidation;

namespace CajaComun.Application.Nomina;

public sealed record ConsultaDescuentos : ConsultaPaginada
{
    public string? Q { get; init; }
    /// <summary>Verdadero deja solo a quienes no tienen descuento por una incidencia; falso, solo a quienes sí.</summary>
    public bool? Suspendido { get; init; }
}

public sealed record DescuentoDto(
    int AhorradorId, string NumEmpleado, string Nombre, decimal Ordinaria, decimal Voluntaria, decimal Prestamo,
    decimal Total, bool Suspendido, IReadOnlyList<TipoIncidencia> Incidencias);

public sealed record ResumenDescuentosDto(
    int Ahorradores, int Suspendidos, decimal Ordinaria, decimal Voluntaria, decimal Prestamo, decimal Total);

public sealed record DescuentosPeriodoDto(
    PeriodoDto Periodo, int CajaId, string Caja, FrecuenciaNomina Frecuencia, string Referencia,
    ResumenDescuentosDto Resumen, Pagina<DescuentoDto> Detalle);

public sealed record IncidenciaDto(
    int Id, int AhorradorId, string NumEmpleado, string Nombre, int PeriodoId, TipoIncidencia Tipo, decimal? Valor, string? Nota);

/// <summary>
/// Valor: nueva cuota ordinaria en CAMBIO_CUOTA. FechaBaja: obligatoria en BAJA y dentro del periodo.
/// </summary>
public sealed record CrearIncidenciaRequest(
    int AhorradorId, int PeriodoId, TipoIncidencia Tipo, decimal? Valor, string? Nota, DateOnly? FechaBaja);

public sealed record AplicarPeriodoResultadoDto(
    int PeriodoId, string Referencia, int AhorradoresConDescuento, int Suspendidos, int MovimientosGenerados,
    decimal AhorroOrdinario, decimal AhorroVoluntario, decimal CapitalPrestamos, decimal InteresPrestamos,
    int PrestamosLiquidados, int CuotasDiferidas);

public enum FormatoLayout { Csv, Xlsx, TxtPipe, TxtAncho }

public sealed record ArchivoGenerado(
    byte[] Contenido, string NombreArchivo, string TipoContenido, int Registros, decimal ImporteTotal, string SumaControl);

public sealed record LayoutDto(
    int Id, TipoLayout Tipo, int CajaId, int? PeriodoId, string? Periodo, string Formato, string NombreArchivo,
    int Registros, decimal ImporteTotal, string? SumaControl, string Usuario, DateTime Fecha);

public sealed record ConsultaLayouts : ConsultaPaginada
{
    public int? CajaId { get; init; }
    public int? PeriodoId { get; init; }
}

public interface INominaService
{
    Task<DescuentosPeriodoDto> DescuentosAsync(int periodoId, ConsultaDescuentos consulta, CancellationToken ct = default);
    Task<IReadOnlyList<IncidenciaDto>> ListarIncidenciasAsync(int periodoId, CancellationToken ct = default);
    Task<IncidenciaDto> CrearIncidenciaAsync(CrearIncidenciaRequest request, CancellationToken ct = default);
    Task EliminarIncidenciaAsync(int id, CancellationToken ct = default);
    Task<AplicarPeriodoResultadoDto> AplicarPeriodoAsync(int periodoId, CancellationToken ct = default);
    Task<ArchivoGenerado> GenerarLayoutAsync(int periodoId, FormatoLayout formato, CancellationToken ct = default);
    Task<Pagina<LayoutDto>> ListarLayoutsAsync(ConsultaLayouts consulta, CancellationToken ct = default);
}

public sealed class CrearIncidenciaRequestValidator : AbstractValidator<CrearIncidenciaRequest>
{
    public CrearIncidenciaRequestValidator()
    {
        RuleFor(x => x.AhorradorId).GreaterThan(0).WithErrorCode("REQUERIDO");
        RuleFor(x => x.PeriodoId).GreaterThan(0).WithErrorCode("REQUERIDO");
        RuleFor(x => x.Tipo).IsInEnum().WithErrorCode("VALOR_INVALIDO");
        RuleFor(x => x.Nota).MaximumLength(500).WithErrorCode("LONGITUD_MAXIMA");
        RuleFor(x => x.Valor).NotNull().WithErrorCode("REQUERIDO")
            .Must(v => v is >= 0m).WithErrorCode("MONTO_NEGATIVO")
            .PrecisionScale(14, 2, true).WithErrorCode("MONTO_INVALIDO")
            .When(x => x.Tipo == TipoIncidencia.CambioCuota);
        RuleFor(x => x.FechaBaja).NotNull().WithErrorCode("REQUERIDO").When(x => x.Tipo == TipoIncidencia.Baja);
    }
}

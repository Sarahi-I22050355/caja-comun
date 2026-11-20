using CajaComun.Application.Comun;
using CajaComun.Domain.Enums;

namespace CajaComun.Application.Importaciones;

public sealed record ErrorFila(string Campo, string Codigo);

public sealed record ResumenImportacionDto(int Filas, int Altas, int Bajas, int SinCambio, int Errores);

public sealed record ImportacionDto(
    int Id, int CajaId, string Caja, string Archivo, FormatoArchivo Formato, string Usuario, DateTime Fecha,
    EstadoImportacion Estado, ResumenImportacionDto Resumen);

public sealed record ImportacionDetalleDto(
    long Id, int NumeroFila, TipoCambioImportacion Tipo, string NumEmpleado, ResultadoValidacion Validacion,
    IReadOnlyList<ErrorFila> Errores, IReadOnlyDictionary<string, string?> Datos);

public sealed record VistaPreviaImportacionDto(ImportacionDto Importacion, Pagina<ImportacionDetalleDto> Detalle);

public sealed record AplicarImportacionResultadoDto(
    int Id, int AltasAplicadas, int BajasAplicadas, int SinCambio, int Omitidas, decimal AhorroLiquidado);

public sealed record ConsultaImportaciones : ConsultaPaginada
{
    public int? CajaId { get; init; }
    public EstadoImportacion? Estado { get; init; }
}

public sealed record ConsultaDetalleImportacion : ConsultaPaginada
{
    public ResultadoValidacion? Validacion { get; init; }
    public TipoCambioImportacion? Tipo { get; init; }
}

public interface IImportacionService
{
    /// <summary>Lee el archivo, valida cada renglón y guarda el lote como vista previa; no cambia el padrón.</summary>
    Task<VistaPreviaImportacionDto> CargarAsync(int cajaId, string nombreArchivo, Stream contenido, CancellationToken ct = default);
    Task<Pagina<ImportacionDto>> ListarAsync(ConsultaImportaciones consulta, CancellationToken ct = default);
    Task<VistaPreviaImportacionDto> ObtenerAsync(int id, ConsultaDetalleImportacion consulta, CancellationToken ct = default);
    /// <summary>Aplica el lote en una sola transacción. Con errores en el lote falla, salvo que se omitan.</summary>
    Task<AplicarImportacionResultadoDto> AplicarAsync(int id, bool omitirErrores, CancellationToken ct = default);
    Task CancelarAsync(int id, CancellationToken ct = default);
}

public static class LimitesImportacion
{
    public const int TamanoMaximoBytes = 2 * 1024 * 1024;
    public const int FilasMaximas = 5_000;
    public static readonly string[] Extensiones = [".xlsx", ".xml", ".txt", ".csv"];
}

using CajaComun.Api.Infraestructura;
using CajaComun.Application.Comun;
using CajaComun.Application.Consultas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CajaComun.Api.Controllers;

/// <summary>
/// Parte 2: las cinco consultas SQL distribuidas (Trino une MariaDB, PostgreSQL y ClickHouse). Solo las ve quien tiene
/// acceso a todas las cajas, porque cada consulta abarca todas.
/// </summary>
[ApiController]
[Authorize(Policy = Politicas.Lectura)]
[Route("api/v1/consultas-distribuidas")]
public class ConsultasDistribuidasController(IConsultasDistribuidasService consultas) : ControllerBase
{
    /// <summary>Consultas disponibles y estado de la federación (cuántos renglones hay en cada motor).</summary>
    [HttpGet]
    public async Task<EstadoFederacionDto> Estado(CancellationToken ct) => await consultas.EstadoAsync(ct);

    /// <summary>Una página del resultado de la consulta federada, con el SQL que se ejecutó y el total real de renglones.</summary>
    [HttpGet("{q:int}")]
    public async Task<ResultadoConsulta> Ejecutar(int q, [FromQuery] int? anio, [FromQuery] int page = 1,
        [FromQuery] int pageSize = ConsultaPaginada.PageSizePorDefecto, CancellationToken ct = default) =>
        await consultas.EjecutarAsync(q, anio, page, pageSize, ct);

    /// <summary>El resultado completo como CSV.</summary>
    [HttpGet("{q:int}/csv")]
    public async Task<IActionResult> Csv(int q, [FromQuery] int? anio, CancellationToken ct)
    {
        var (nombre, contenido) = await consultas.ExportarCsvAsync(q, anio, ct);
        return File(contenido, "text/csv; charset=utf-8", nombre);
    }

    /// <summary>Compara el resultado federado con el de la misma consulta directa sobre MariaDB.</summary>
    [HttpGet("{q:int}/equivalencia")]
    public async Task<EquivalenciaDto> Equivalencia(int q, [FromQuery] int? anio, CancellationToken ct) =>
        await consultas.VerificarEquivalenciaAsync(q, anio, ct);
}

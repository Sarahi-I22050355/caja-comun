using CajaComun.Api.Infraestructura;
using CajaComun.Application.Comun;
using CajaComun.Application.Nomina;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CajaComun.Api.Controllers;

[ApiController]
[Route("api/v1/nomina")]
[Authorize(Policy = Politicas.Lectura)]
public class NominaController(INominaService nomina) : ControllerBase
{
    [HttpGet("periodos/{periodoId:int}/descuentos")]
    public async Task<DescuentosPeriodoDto> Descuentos(int periodoId, [FromQuery] ConsultaDescuentos consulta, CancellationToken ct) =>
        await nomina.DescuentosAsync(periodoId, consulta, ct);

    [HttpGet("periodos/{periodoId:int}/incidencias")]
    public async Task<IReadOnlyList<IncidenciaDto>> Incidencias(int periodoId, CancellationToken ct) =>
        await nomina.ListarIncidenciasAsync(periodoId, ct);

    [HttpPost("incidencias")]
    [Authorize(Policy = Politicas.Captura)]
    public async Task<ActionResult<IncidenciaDto>> CrearIncidencia(CrearIncidenciaRequest request, CancellationToken ct)
    {
        var creada = await nomina.CrearIncidenciaAsync(request, ct);
        return StatusCode(StatusCodes.Status201Created, creada);
    }

    [HttpDelete("incidencias/{id:int}")]
    [Authorize(Policy = Politicas.Captura)]
    public async Task<IActionResult> EliminarIncidencia(int id, CancellationToken ct)
    {
        await nomina.EliminarIncidenciaAsync(id, ct);
        return NoContent();
    }

    /// <summary>Genera los movimientos del periodo y lo marca como aplicado. Solo una vez por periodo.</summary>
    [HttpPost("periodos/{periodoId:int}/aplicar")]
    [Authorize(Policy = Politicas.Administracion)]
    public async Task<AplicarPeriodoResultadoDto> Aplicar(int periodoId, CancellationToken ct) =>
        await nomina.AplicarPeriodoAsync(periodoId, ct);

    /// <summary>Descarga el layout de incidencias; el resumen del archivo viaja en cabeceras X-*.</summary>
    [HttpPost("periodos/{periodoId:int}/layout")]
    [Authorize(Policy = Politicas.Captura)]
    public async Task<IActionResult> Layout(int periodoId, [FromQuery] FormatoLayout formato = FormatoLayout.Csv, CancellationToken ct = default)
    {
        var archivo = await nomina.GenerarLayoutAsync(periodoId, formato, ct);
        Response.Headers["X-Registros"] = archivo.Registros.ToString();
        Response.Headers["X-Importe-Total"] = archivo.ImporteTotal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        Response.Headers["X-Suma-Control"] = archivo.SumaControl;
        Response.Headers.AccessControlExposeHeaders = "Content-Disposition, X-Registros, X-Importe-Total, X-Suma-Control";
        return File(archivo.Contenido, archivo.TipoContenido, archivo.NombreArchivo);
    }

    [HttpGet("layouts")]
    public async Task<Pagina<LayoutDto>> Layouts([FromQuery] ConsultaLayouts consulta, CancellationToken ct) =>
        await nomina.ListarLayoutsAsync(consulta, ct);
}

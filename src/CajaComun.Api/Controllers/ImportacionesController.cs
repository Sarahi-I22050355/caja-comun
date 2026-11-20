using CajaComun.Api.Infraestructura;
using CajaComun.Application.Comun;
using CajaComun.Application.Importaciones;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CajaComun.Api.Controllers;

[ApiController]
[Route("api/v1/importaciones")]
[Authorize(Policy = Politicas.Lectura)]
public class ImportacionesController(IImportacionService importaciones) : ControllerBase
{
    /// <summary>Sube un archivo de altas y bajas y devuelve la vista previa; no cambia el padrón hasta aplicar.</summary>
    [HttpPost]
    [Authorize(Policy = Politicas.Captura)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(LimitesImportacion.TamanoMaximoBytes + 256 * 1024)]
    public async Task<ActionResult<VistaPreviaImportacionDto>> Cargar(
        [FromForm] int cajaId, IFormFile archivo, CancellationToken ct)
    {
        await using var flujo = archivo.OpenReadStream();
        var vista = await importaciones.CargarAsync(cajaId, archivo.FileName, flujo, ct);
        return CreatedAtAction(nameof(Obtener), new { id = vista.Importacion.Id }, vista);
    }

    [HttpGet]
    public async Task<Pagina<ImportacionDto>> Listar([FromQuery] ConsultaImportaciones consulta, CancellationToken ct) =>
        await importaciones.ListarAsync(consulta, ct);

    [HttpGet("{id:int}")]
    public async Task<VistaPreviaImportacionDto> Obtener(int id, [FromQuery] ConsultaDetalleImportacion consulta, CancellationToken ct) =>
        await importaciones.ObtenerAsync(id, consulta, ct);

    [HttpPost("{id:int}/aplicar")]
    [Authorize(Policy = Politicas.Captura)]
    public async Task<AplicarImportacionResultadoDto> Aplicar(int id, [FromQuery] bool omitirErrores, CancellationToken ct) =>
        await importaciones.AplicarAsync(id, omitirErrores, ct);

    [HttpPost("{id:int}/cancelar")]
    [Authorize(Policy = Politicas.Captura)]
    public async Task<IActionResult> Cancelar(int id, CancellationToken ct)
    {
        await importaciones.CancelarAsync(id, ct);
        return NoContent();
    }
}

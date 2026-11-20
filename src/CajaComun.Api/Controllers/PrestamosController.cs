using CajaComun.Api.Infraestructura;
using CajaComun.Application.Comun;
using CajaComun.Application.Prestamos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CajaComun.Api.Controllers;

[ApiController]
[Route("api/v1/prestamos")]
[Authorize(Policy = Politicas.Lectura)]
public class PrestamosController(IPrestamoService prestamos) : ControllerBase
{
    /// <summary>Evalúa la garantía y calcula la amortización sin guardar nada.</summary>
    [HttpPost("simular")]
    public async Task<SimulacionPrestamoDto> Simular(SolicitudPrestamoRequest request, CancellationToken ct) =>
        await prestamos.SimularAsync(request, ct);

    [HttpPost]
    [Authorize(Policy = Politicas.Captura)]
    public async Task<ActionResult<PrestamoDetalleDto>> Crear(SolicitudPrestamoRequest request, CancellationToken ct)
    {
        var creado = await prestamos.CrearAsync(request, ct);
        return CreatedAtAction(nameof(Obtener), new { id = creado.Prestamo.Id }, creado);
    }

    [HttpGet]
    public async Task<Pagina<PrestamoDto>> Listar([FromQuery] ConsultaPrestamos consulta, CancellationToken ct) =>
        await prestamos.ListarAsync(consulta, ct);

    [HttpGet("resumen")]
    public async Task<CarteraResumenDto> Resumen([FromQuery] int? cajaId, CancellationToken ct) =>
        await prestamos.ResumenAsync(cajaId, ct);

    [HttpGet("{id:int}")]
    public async Task<PrestamoDetalleDto> Obtener(int id, CancellationToken ct) => await prestamos.ObtenerAsync(id, ct);

    [HttpPost("{id:int}/autorizar")]
    [Authorize(Policy = Politicas.Administracion)]
    public async Task<PrestamoDetalleDto> Autorizar(int id, CancellationToken ct) => await prestamos.AutorizarAsync(id, ct);

    [HttpPost("{id:int}/rechazar")]
    [Authorize(Policy = Politicas.Administracion)]
    public async Task<PrestamoDetalleDto> Rechazar(int id, CancellationToken ct) => await prestamos.RechazarAsync(id, ct);
}

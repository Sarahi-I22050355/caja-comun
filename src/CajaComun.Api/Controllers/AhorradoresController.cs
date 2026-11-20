using CajaComun.Api.Infraestructura;
using CajaComun.Application.Ahorradores;
using CajaComun.Application.Comun;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CajaComun.Api.Controllers;

[ApiController]
[Route("api/v1/ahorradores")]
[Authorize(Policy = Politicas.Lectura)]
public class AhorradoresController(IAhorradorService ahorradores) : ControllerBase
{
    [HttpGet]
    public async Task<Pagina<AhorradorListaDto>> Listar([FromQuery] ConsultaAhorradores consulta, CancellationToken ct) =>
        await ahorradores.ListarAsync(consulta, ct);

    [HttpGet("{id:int}")]
    public async Task<AhorradorDetalleDto> Obtener(int id, CancellationToken ct) => await ahorradores.ObtenerAsync(id, ct);

    [HttpPost]
    [Authorize(Policy = Politicas.Captura)]
    public async Task<ActionResult<AhorradorDetalleDto>> Crear(CrearAhorradorRequest request, CancellationToken ct)
    {
        var creado = await ahorradores.CrearAsync(request, ct);
        return CreatedAtAction(nameof(Obtener), new { id = creado.Id }, creado);
    }

    [HttpPut("{id:int}")]
    [Authorize(Policy = Politicas.Captura)]
    public async Task<AhorradorDetalleDto> Actualizar(int id, ActualizarAhorradorRequest request, CancellationToken ct) =>
        await ahorradores.ActualizarAsync(id, request, ct);

    [HttpGet("{id:int}/movimientos")]
    public async Task<Pagina<MovimientoDto>> Movimientos(int id, [FromQuery] ConsultaMovimientos consulta, CancellationToken ct) =>
        await ahorradores.ListarMovimientosAsync(id, consulta, ct);

    [HttpGet("{id:int}/estado-cuenta")]
    public async Task<EstadoCuentaDto> EstadoCuenta(int id, [FromQuery] int? anio, CancellationToken ct)
    {
        var ciclo = anio ?? DateTime.UtcNow.Year;
        if (ciclo is < 2000 or > 2100)
            throw new ErrorNegocio(Codigos.Validacion, "El año no es válido.", 400,
                new Dictionary<string, string[]> { ["anio"] = ["ANIO_INVALIDO"] });
        return await ahorradores.EstadoCuentaAsync(id, ciclo, ct);
    }

    [HttpGet("{id:int}/cuotas")]
    public async Task<IReadOnlyList<CuotaDto>> Cuotas(int id, CancellationToken ct) => await ahorradores.ListarCuotasAsync(id, ct);

    [HttpPost("{id:int}/cuotas")]
    [Authorize(Policy = Politicas.Captura)]
    public async Task<ActionResult<CuotaDto>> CrearCuota(int id, CrearCuotaRequest request, CancellationToken ct)
    {
        var cuota = await ahorradores.CrearCuotaAsync(id, request, ct);
        return CreatedAtAction(nameof(Cuotas), new { id }, cuota);
    }
}

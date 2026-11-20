using CajaComun.Api.Infraestructura;
using CajaComun.Application.Cajas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CajaComun.Api.Controllers;

[ApiController]
[Authorize(Policy = Politicas.Lectura)]
public class CajasController(ICajaService cajas) : ControllerBase
{
    [HttpGet("api/v1/cajas")]
    public async Task<IReadOnlyList<CajaDto>> Listar(CancellationToken ct) => await cajas.ListarAsync(ct);

    [HttpGet("api/v1/cajas/{id:int}")]
    public async Task<CajaDto> Obtener(int id, CancellationToken ct) => await cajas.ObtenerAsync(id, ct);

    [HttpPost("api/v1/cajas")]
    [Authorize(Policy = Politicas.Administracion)]
    public async Task<ActionResult<CajaDto>> Crear(CrearCajaRequest request, CancellationToken ct)
    {
        var caja = await cajas.CrearAsync(request, ct);
        return CreatedAtAction(nameof(Obtener), new { id = caja.Id }, caja);
    }

    [HttpPut("api/v1/cajas/{id:int}")]
    [Authorize(Policy = Politicas.Administracion)]
    public async Task<CajaDto> Actualizar(int id, ActualizarCajaRequest request, CancellationToken ct) =>
        await cajas.ActualizarAsync(id, request, ct);

    [HttpGet("api/v1/cajas/{id:int}/periodos")]
    public async Task<IReadOnlyList<PeriodoDto>> Periodos(int id, [FromQuery] int? anio, CancellationToken ct) =>
        await cajas.ListarPeriodosAsync(id, anio, ct);

    /// <summary>Catálogo de empresas para el alta de cajas.</summary>
    [HttpGet("api/v1/empresas")]
    [Authorize(Policy = Politicas.Administracion)]
    public async Task<IReadOnlyList<EmpresaDto>> Empresas(CancellationToken ct) => await cajas.ListarEmpresasAsync(ct);
}

using CajaComun.Api.Infraestructura;
using CajaComun.Application.Comun;
using CajaComun.Application.Movimientos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CajaComun.Api.Controllers;

[ApiController]
[Route("api/v1/movimientos")]
[Authorize(Policy = Politicas.Lectura)]
public class MovimientosController(IMovimientoService movimientos) : ControllerBase
{
    /// <summary>Solo lectura: los movimientos los generan la nómina, los préstamos y el cierre de ciclo.</summary>
    [HttpGet]
    public async Task<Pagina<MovimientoCajaDto>> Listar([FromQuery] ConsultaMovimientosCaja consulta, CancellationToken ct) =>
        await movimientos.ListarAsync(consulta, ct);
}

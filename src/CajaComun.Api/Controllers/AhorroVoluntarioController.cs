using CajaComun.Api.Infraestructura;
using CajaComun.Application.AhorroVoluntario;
using CajaComun.Application.Comun;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CajaComun.Api.Controllers;

[ApiController]
[Route("api/v1/ahorro-voluntario")]
[Authorize(Policy = Politicas.Lectura)]
public class AhorroVoluntarioController(IAhorroVoluntarioService voluntario) : ControllerBase
{
    [HttpGet]
    public async Task<Pagina<VoluntarioDto>> Listar([FromQuery] ConsultaVoluntario consulta, CancellationToken ct) =>
        await voluntario.ListarAsync(consulta, ct);

    [HttpGet("resumen")]
    public async Task<ResumenVoluntarioDto> Resumen([FromQuery] int? cajaId, CancellationToken ct) =>
        await voluntario.ResumenAsync(cajaId, ct);

    [HttpPut("{ahorradorId:int}")]
    [Authorize(Policy = Politicas.Captura)]
    public async Task<VoluntarioDto> Actualizar(int ahorradorId, ActualizarVoluntarioRequest request, CancellationToken ct) =>
        await voluntario.ActualizarAsync(ahorradorId, request, ct);
}

using CajaComun.Api.Infraestructura;
using CajaComun.Application.Usuarios;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CajaComun.Api.Controllers;

[ApiController]
[Route("api/v1/usuarios")]
[Authorize(Policy = Politicas.Administracion)]
public class UsuariosController(IUsuarioService usuarios) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<UsuarioDto>> Listar(CancellationToken ct) => await usuarios.ListarAsync(ct);

    [HttpGet("{id:int}")]
    public async Task<UsuarioDto> Obtener(int id, CancellationToken ct) => await usuarios.ObtenerAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<UsuarioDto>> Crear(CrearUsuarioRequest request, CancellationToken ct)
    {
        var usuario = await usuarios.CrearAsync(request, ct);
        return CreatedAtAction(nameof(Obtener), new { id = usuario.Id }, usuario);
    }

    [HttpPut("{id:int}")]
    public async Task<UsuarioDto> Actualizar(int id, ActualizarUsuarioRequest request, CancellationToken ct) =>
        await usuarios.ActualizarAsync(id, request, ct);

    [HttpPost("{id:int}/password")]
    public async Task<IActionResult> RestablecerPassword(int id, RestablecerPasswordRequest request, CancellationToken ct)
    {
        await usuarios.RestablecerPasswordAsync(id, request, ct);
        return NoContent();
    }
}

using CajaComun.Api.Infraestructura;
using CajaComun.Application.Comun;
using CajaComun.Application.Reportes;
using CajaComun.Application.Tareas;
using CajaComun.Application.Trabajos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CajaComun.Api.Controllers;

[ApiController]
[Route("api/v1/tareas")]
[Authorize(Policy = Politicas.Lectura)]
public class TareasController(ITareaService tareas) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<TareaDto>> Listar(CancellationToken ct) => await tareas.ListarAsync(ct);

    [HttpGet("{id:int}")]
    public async Task<TareaDto> Obtener(int id, CancellationToken ct) => await tareas.ObtenerAsync(id, ct);

    /// <summary>Cambia frecuencia, hora o interruptor; la tarea se reprograma de inmediato.</summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Politicas.Administracion)]
    public async Task<TareaDto> Actualizar(int id, ActualizarTareaRequest request, CancellationToken ct) =>
        await tareas.ActualizarAsync(id, request, ct);

    /// <summary>Ejecuta la tarea ya, en segundo plano: 202 con el id del trabajo.</summary>
    [HttpPost("{id:int}/ejecutar")]
    [Authorize(Policy = Politicas.Administracion)]
    public async Task<IActionResult> Ejecutar(int id, CancellationToken ct)
    {
        var trabajo = await tareas.EjecutarAhoraAsync(id, ct);
        return AcceptedAtAction("Obtener", "Trabajos", new { id = trabajo.Id }, trabajo);
    }

    [HttpGet("{id:int}/historial")]
    public async Task<Pagina<EjecucionDto>> Historial(int id, [FromQuery] ConsultaPaginada consulta, CancellationToken ct) =>
        await tareas.HistorialAsync(id, consulta, ct);
}

[ApiController]
[Route("api/v1/trabajos")]
[Authorize(Policy = Politicas.Lectura)]
public class TrabajosController(IColaTrabajos cola, IUsuarioActual usuario) : ControllerBase
{
    /// <summary>Estado de un trabajo en segundo plano. Solo lo ve quien lo pidió o un Administrador.</summary>
    [HttpGet("{id}")]
    public async Task<TrabajoDto> Obtener(string id, CancellationToken ct) =>
        await cola.ObtenerAsync(id, usuario.Id, usuario.Rol, ct)
        ?? throw ErrorNegocio.NoEncontrado(Codigos.TrabajoNoEncontrado, "El trabajo no existe o ya venció.");

    [HttpGet("{id}/archivo")]
    public async Task<IActionResult> Archivo(string id, CancellationToken ct)
    {
        var trabajo = await cola.ObtenerAsync(id, usuario.Id, usuario.Rol, ct)
            ?? throw ErrorNegocio.NoEncontrado(Codigos.TrabajoNoEncontrado, "El trabajo no existe o ya venció.");
        if (trabajo.Estado is not EstadoTrabajo.Exito)
            throw ErrorNegocio.Conflicto(Codigos.TrabajoNoTerminado, "El trabajo todavía no termina o no generó archivo.");
        var archivo = await cola.ArchivoAsync(id, usuario.Id, usuario.Rol, ct)
            ?? throw ErrorNegocio.NoEncontrado(Codigos.TrabajoNoEncontrado, "El trabajo no generó un archivo.");
        return File(archivo.Contenido, archivo.TipoContenido, archivo.NombreArchivo);
    }
}

[ApiController]
[Route("api/v1/dashboard")]
[Authorize(Policy = Politicas.Lectura)]
public class DashboardController(IDashboardService dashboard) : ControllerBase
{
    [HttpGet]
    public async Task<DashboardDto> Obtener([FromQuery] int? cajaId, CancellationToken ct) =>
        await dashboard.ObtenerAsync(cajaId, ct);
}

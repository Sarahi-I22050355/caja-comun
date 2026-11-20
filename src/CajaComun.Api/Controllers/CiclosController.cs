using CajaComun.Api.Infraestructura;
using CajaComun.Application.Ciclos;
using CajaComun.Application.Comun;
using CajaComun.Application.Nomina;
using CajaComun.Application.Trabajos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace CajaComun.Api.Controllers;

[ApiController]
[Route("api/v1/ciclos")]
[Authorize(Policy = Politicas.Lectura)]
public class CiclosController(ICicloService ciclos, IColaTrabajos cola, IUsuarioActual usuario) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<CicloDto>> Listar([FromQuery] int? cajaId, CancellationToken ct) =>
        await ciclos.ListarAsync(cajaId, ct);

    [HttpGet("{id:int}")]
    public async Task<CicloResumenDto> Resumen(int id, CancellationToken ct) => await ciclos.ResumenAsync(id, ct);

    [HttpGet("{id:int}/distribucion")]
    public async Task<Pagina<DistribucionDto>> Distribucion(int id, [FromQuery] ConsultaDistribucion consulta, CancellationToken ct) =>
        await ciclos.DistribucionAsync(id, consulta, ct);

    /// <summary>Calcula o recalcula la distribución provisional del ciclo (pasos 1 y 2 del cierre).</summary>
    [HttpPost("borrador")]
    [Authorize(Policy = Politicas.Administracion)]
    public async Task<CicloResumenDto> Borrador(BorradorCicloRequest request, CancellationToken ct) =>
        await ciclos.BorradorAsync(request, ct);

    /// <summary>Cierra el ciclo como trabajo en segundo plano: responde 202 con el id para consultar en /trabajos/{id}.</summary>
    [HttpPost("{id:int}/cerrar")]
    [Authorize(Policy = Politicas.Administracion)]
    public async Task<IActionResult> Cerrar(int id, CancellationToken ct)
    {
        await ciclos.ResumenAsync(id, ct);       // 404 si no existe o es de una caja ajena, antes de encolar
        var trabajo = await cola.EncolarAsync("CIERRE_CICLO", usuario.Id, usuario.Rol,
            async (servicios, token) => new ResultadoTrabajo(
                await servicios.GetRequiredService<ICicloService>().CerrarAsync(id, token)), ct);
        return AcceptedAtAction("Obtener", "Trabajos", new { id = trabajo.Id }, trabajo);
    }

    /// <summary>Archivo de dispersión del ciclo cerrado. <c>incluirAhorro=false</c> deja solo el rendimiento.</summary>
    [HttpPost("{id:int}/layout-spei")]
    [Authorize(Policy = Politicas.Administracion)]
    public async Task<IActionResult> LayoutSpei(
        int id, [FromQuery] FormatoSpei formato = FormatoSpei.GenericoCsv, [FromQuery] bool incluirAhorro = true, CancellationToken ct = default)
    {
        var archivo = await ciclos.GenerarSpeiAsync(id, formato, incluirAhorro, ct);
        Response.Headers["X-Registros"] = archivo.Registros.ToString();
        Response.Headers["X-Importe-Total"] = archivo.ImporteTotal.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
        Response.Headers["X-Suma-Control"] = archivo.SumaControl;
        Response.Headers.AccessControlExposeHeaders = "Content-Disposition, X-Registros, X-Importe-Total, X-Suma-Control";
        return File(archivo.Contenido, archivo.TipoContenido, archivo.NombreArchivo);
    }

    [HttpGet("{id:int}/layouts")]
    public async Task<Pagina<LayoutDto>> Layouts(int id, [FromQuery] ConsultaPaginada consulta, CancellationToken ct) =>
        await ciclos.ListarLayoutsAsync(id, consulta, ct);
}

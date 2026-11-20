using CajaComun.Api.Infraestructura;
using CajaComun.Application.Ahorradores;
using CajaComun.Application.Ciclos;
using CajaComun.Application.Comun;
using CajaComun.Application.Nomina;
using CajaComun.Application.Reportes;
using CajaComun.Application.Trabajos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace CajaComun.Api.Controllers;

[ApiController]
[Route("api/v1/reportes")]
[Authorize(Policy = Politicas.Lectura)]
public class ReportesController(
    IReporteService reportes, IAhorradorService ahorradores, ICicloService ciclos, IColaTrabajos cola, IUsuarioActual usuario) : ControllerBase
{
    private static int Anio(int? anio)
    {
        var a = anio ?? DateTime.UtcNow.Year;
        if (a is < 2000 or > 2100)
            throw new ErrorNegocio(Codigos.Validacion, "El año no es válido.", 400,
                new Dictionary<string, string[]> { ["anio"] = ["ANIO_INVALIDO"] });
        return a;
    }

    private FileContentResult Archivo(ArchivoGenerado a) => File(a.Contenido, a.TipoContenido, a.NombreArchivo);

    /// <summary>Estado de cuenta de un ahorrador. En JSON es la misma respuesta que /ahorradores/{id}/estado-cuenta.</summary>
    [HttpGet("estado-cuenta/{ahorradorId:int}")]
    public async Task<IActionResult> EstadoCuenta(int ahorradorId, [FromQuery] int? anio, [FromQuery] FormatoReporte formato = FormatoReporte.Json, CancellationToken ct = default) =>
        formato == FormatoReporte.Json
            ? Ok(await ahorradores.EstadoCuentaAsync(ahorradorId, Anio(anio), ct))
            : Archivo(await reportes.EstadoCuentaArchivoAsync(ahorradorId, Anio(anio), formato, ct));

    /// <summary>Ingresos por intereses (devengados y cobrados) y utilidad a distribuir de una caja en un ejercicio.</summary>
    [HttpGet("estado-resultados")]
    public async Task<IActionResult> EstadoResultados([FromQuery] int cajaId, [FromQuery] int? anio, [FromQuery] FormatoReporte formato = FormatoReporte.Json, CancellationToken ct = default) =>
        formato == FormatoReporte.Json
            ? Ok(await reportes.EstadoResultadosAsync(cajaId, Anio(anio), ct))
            : Archivo(await reportes.EstadoResultadosArchivoAsync(cajaId, Anio(anio), formato, ct));

    /// <summary>Distribución de intereses de un ciclo. En JSON va paginada; PDF y Excel traen todos los renglones.</summary>
    [HttpGet("distribucion")]
    public async Task<IActionResult> Distribucion([FromQuery] int cicloId, [FromQuery] ConsultaDistribucion consulta, [FromQuery] FormatoReporte formato = FormatoReporte.Json, CancellationToken ct = default) =>
        formato == FormatoReporte.Json
            ? Ok(await ciclos.DistribucionAsync(cicloId, consulta, ct))
            : Archivo(await reportes.DistribucionArchivoAsync(cicloId, formato, ct));

    /// <summary>Genera el reporte en segundo plano (para los grandes): 202 con el id; el archivo se descarga de /trabajos/{id}/archivo.</summary>
    [HttpPost("trabajos")]
    public async Task<IActionResult> Trabajo(ReporteTrabajoRequest request, CancellationToken ct)
    {
        var trabajo = await cola.EncolarAsync("REPORTE", usuario.Id, usuario.Rol,
            async (servicios, token) =>
            {
                var archivo = await servicios.GetRequiredService<IReporteService>().GenerarAsync(request, token);
                return new ResultadoTrabajo(new { archivo.NombreArchivo, archivo.Registros }, archivo);
            }, ct);
        return AcceptedAtAction("Obtener", "Trabajos", new { id = trabajo.Id }, trabajo);
    }
}

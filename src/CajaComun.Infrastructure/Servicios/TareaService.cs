using CajaComun.Application.Comun;
using CajaComun.Application.Tareas;
using CajaComun.Application.Trabajos;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CajaComun.Infrastructure.Servicios;

public sealed class TareaService(
    CajaComunDbContext db, IProgramadorTareas programador, IColaTrabajos cola, IUsuarioActual usuario) : ITareaService
{
    private sealed class FilaTarea
    {
        public int Id { get; set; }
        public string Clave { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string Frecuencia { get; set; } = "";
        public TimeSpan Hora { get; set; }
        public bool Activa { get; set; }
        public DateTime? UltimaEjecucion { get; set; }
        public DateTime? ProximaEjecucion { get; set; }
        public string? UltimoEstado { get; set; }
        public string? UltimoResumen { get; set; }
    }

    private const string SelectTarea = """
        SELECT t.id AS Id, t.clave AS Clave, t.nombre AS Nombre, t.frecuencia AS Frecuencia, t.hora AS Hora, t.activa AS Activa,
               t.ultima_ejecucion AS UltimaEjecucion, t.proxima_ejecucion AS ProximaEjecucion,
               e.estado AS UltimoEstado, e.resumen AS UltimoResumen
          FROM tarea_programada t
          LEFT JOIN tarea_ejecucion e
            ON e.id = (SELECT MAX(x.id) FROM tarea_ejecucion x WHERE x.tarea_id = t.id)
        """;

    private static TareaDto Mapear(FilaTarea f) => new(
        f.Id, f.Clave, f.Nombre, ConversionesEnum.DeDb<FrecuenciaTarea>(f.Frecuencia), TimeOnly.FromTimeSpan(f.Hora), f.Activa,
        f.UltimaEjecucion is { } u ? DateTime.SpecifyKind(u, DateTimeKind.Utc) : null,
        f.ProximaEjecucion is { } p ? DateTime.SpecifyKind(p, DateTimeKind.Utc) : null,
        f.UltimoEstado is null ? null : ConversionesEnum.DeDb<EstadoEjecucion>(f.UltimoEstado), f.UltimoResumen);

    public async Task<IReadOnlyList<TareaDto>> ListarAsync(CancellationToken ct = default) =>
        (await db.ConsultarAsync<FilaTarea>($"{SelectTarea} ORDER BY t.nombre", null, ct)).Select(Mapear).ToList();

    public async Task<TareaDto> ObtenerAsync(int id, CancellationToken ct = default) =>
        (await db.ConsultarAsync<FilaTarea>($"{SelectTarea} WHERE t.id = @id", new { id }, ct)).Select(Mapear).FirstOrDefault()
        ?? throw ErrorNegocio.NoEncontrado(Codigos.TareaNoEncontrada, "La tarea no existe.");

    public async Task<TareaDto> ActualizarAsync(int id, ActualizarTareaRequest request, CancellationToken ct = default)
    {
        var tarea = await db.Tareas.FirstOrDefaultAsync(t => t.Id == id, ct)
            ?? throw ErrorNegocio.NoEncontrado(Codigos.TareaNoEncontrada, "La tarea no existe.");
        tarea.Frecuencia = request.Frecuencia;
        tarea.Hora = new TimeOnly(request.Hora.Hour, request.Hora.Minute);
        tarea.Activa = request.Activa;
        await db.SaveChangesAsync(ct);

        // Se reprograma en caliente: Quartz toma la nueva frecuencia y hora sin reiniciar y se guarda la próxima ejecución.
        await programador.ReprogramarAsync(id, ct);
        db.ChangeTracker.Clear();
        return await ObtenerAsync(id, ct);
    }

    public async Task<TrabajoDto> EjecutarAhoraAsync(int id, CancellationToken ct = default)
    {
        if (!await db.Tareas.AnyAsync(t => t.Id == id, ct))
            throw ErrorNegocio.NoEncontrado(Codigos.TareaNoEncontrada, "La tarea no existe.");
        return await cola.EncolarAsync("TAREA", usuario.Id, usuario.Rol, async (servicios, token) =>
        {
            var ejecutor = (IEjecutorTareas)servicios.GetService(typeof(IEjecutorTareas))!;
            return new ResultadoTrabajo(await ejecutor.EjecutarAsync(id, token));
        }, ct);
    }

    public async Task<Pagina<EjecucionDto>> HistorialAsync(int id, ConsultaPaginada consulta, CancellationToken ct = default)
    {
        if (!await db.Tareas.AnyAsync(t => t.Id == id, ct))
            throw ErrorNegocio.NoEncontrado(Codigos.TareaNoEncontrada, "La tarea no existe.");
        var q = db.TareaEjecuciones.AsNoTracking().Where(e => e.TareaId == id);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(e => e.Id).Skip(consulta.Saltar).Take(consulta.TamanoSeguro)
            .Select(e => new EjecucionDto(e.Id, e.Inicio, e.Fin, e.Estado, e.Resumen)).ToListAsync(ct);
        return new Pagina<EjecucionDto>(items, consulta.PaginaSegura, consulta.TamanoSeguro, total);
    }
}

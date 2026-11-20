using CajaComun.Application.Comun;
using CajaComun.Application.Tareas;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;
using CajaComun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;

namespace CajaComun.Infrastructure.Tareas;

/// <summary>Cómo se traduce la frecuencia configurada a una expresión cron de Quartz.</summary>
public static class PlanTarea
{
    /// <summary>
    /// Diaria: todos los días a la hora. Quincenal: los días 1 y 16 (inicio de cada quincena). Mensual: el día 1.
    /// La hora es la de la Ciudad de México.
    /// </summary>
    public static string Cron(FrecuenciaTarea frecuencia, TimeOnly hora) => frecuencia switch
    {
        FrecuenciaTarea.Diaria => $"0 {hora.Minute} {hora.Hour} * * ?",
        FrecuenciaTarea.Quincenal => $"0 {hora.Minute} {hora.Hour} 1,16 * ?",
        FrecuenciaTarea.Mensual => $"0 {hora.Minute} {hora.Hour} 1 * ?",
        _ => throw new ArgumentOutOfRangeException(nameof(frecuencia))
    };

    /// <summary>México no tiene horario de verano desde 2022; si el sistema no trae la zona se usa UTC-6 fijo.</summary>
    public static TimeZoneInfo Zona()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City"); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("America/Mexico_City", TimeSpan.FromHours(-6), "Ciudad de México", "CST");
        }
    }
}

/// <summary>Programador que no hace nada: cuando las tareas están deshabilitadas por configuración.</summary>
public sealed class ProgramadorInactivo : IProgramadorTareas
{
    public Task ReprogramarAsync(int tareaId, CancellationToken ct = default) => Task.CompletedTask;
    public Task<DateTime?> ProximaEjecucionUtcAsync(int tareaId, CancellationToken ct = default) => Task.FromResult<DateTime?>(null);
}

/// <summary>Trabajo de Quartz: avisa al ejecutor cuál tarea toca. Una ejecución a la vez por tarea.</summary>
[DisallowConcurrentExecution]
public sealed class TareaJob(IServiceScopeFactory ambitos) : IJob
{
    public async Task Execute(IJobExecutionContext context)
    {
        var tareaId = context.MergedJobDataMap.GetInt("tareaId");
        await using var ambito = ambitos.CreateAsyncScope();
        await ambito.ServiceProvider.GetRequiredService<IEjecutorTareas>().EjecutarAsync(tareaId, context.CancellationToken);
    }
}

/// <summary>
/// La configuración de cada tarea (frecuencia, hora, activa) está en la base de datos; este servicio la traslada a Quartz y
/// la vuelve a trasladar cada vez que cambia, así que la reprogramación surte efecto sin reiniciar la API.
/// </summary>
public sealed class ProgramadorTareas(ISchedulerFactory fabrica, IServiceScopeFactory ambitos, ILogger<ProgramadorTareas> log)
    : IProgramadorTareas
{
    private static JobKey Trabajo(int id) => new($"tarea-{id}", "tareas");
    private static TriggerKey Disparador(int id) => new($"tarea-{id}", "tareas");

    public async Task ReprogramarAsync(int tareaId, CancellationToken ct = default)
    {
        await using var ambito = ambitos.CreateAsyncScope();
        var db = ambito.ServiceProvider.GetRequiredService<CajaComunDbContext>();
        var tarea = await db.Tareas.FirstOrDefaultAsync(t => t.Id == tareaId, ct);

        var scheduler = await fabrica.GetScheduler(ct);
        await scheduler.DeleteJob(Trabajo(tareaId), ct);
        if (tarea is null || !tarea.Activa)
        {
            if (tarea is not null) await GuardarProximaAsync(db, tarea, null, ct);
            return;
        }

        var job = JobBuilder.Create<TareaJob>().WithIdentity(Trabajo(tareaId)).UsingJobData("tareaId", tareaId).Build();
        var trigger = TriggerBuilder.Create().WithIdentity(Disparador(tareaId))
            .WithCronSchedule(PlanTarea.Cron(tarea.Frecuencia, tarea.Hora),
                c => c.InTimeZone(PlanTarea.Zona()).WithMisfireHandlingInstructionDoNothing())
            .Build();
        var proxima = await scheduler.ScheduleJob(job, trigger, ct);
        await GuardarProximaAsync(db, tarea, proxima.UtcDateTime, ct);
        log.LogInformation("Tarea {Clave} programada ({Frecuencia} a las {Hora}); próxima ejecución {Proxima:u}",
            tarea.Clave, tarea.Frecuencia, tarea.Hora, proxima);
    }

    public async Task<DateTime?> ProximaEjecucionUtcAsync(int tareaId, CancellationToken ct = default)
    {
        var scheduler = await fabrica.GetScheduler(ct);
        var trigger = await scheduler.GetTrigger(Disparador(tareaId), ct);
        return trigger?.GetNextFireTimeUtc()?.UtcDateTime;
    }

    private static async Task GuardarProximaAsync(CajaComunDbContext db, TareaProgramada tarea, DateTime? proxima, CancellationToken ct)
    {
        tarea.ProximaEjecucion = proxima;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Compara la configuración de la base con lo programado en Quartz y corrige las diferencias: tareas nuevas, cambios
    /// hechos fuera de la API (por ejemplo, tras sembrar la base con la API ya encendida) y tareas pausadas.
    /// </summary>
    public async Task SincronizarAsync(CancellationToken ct)
    {
        List<TareaProgramada> tareas;
        await using (var ambito = ambitos.CreateAsyncScope())
            tareas = await ambito.ServiceProvider.GetRequiredService<CajaComunDbContext>().Tareas.AsNoTracking().ToListAsync(ct);

        var scheduler = await fabrica.GetScheduler(ct);
        foreach (var t in tareas)
        {
            var trigger = await scheduler.GetTrigger(Disparador(t.Id), ct) as ICronTrigger;
            var esperado = t.Activa ? PlanTarea.Cron(t.Frecuencia, t.Hora) : null;
            // También se corrige la próxima ejecución guardada: si la base se reinició, Quartz conserva su programación pero
            // la tabla ya no la tiene.
            var proximaIncoherente = t.Activa ? t.ProximaEjecucion is null : t.ProximaEjecucion is not null;
            if (trigger?.CronExpressionString != esperado || proximaIncoherente) await ReprogramarAsync(t.Id, ct);
        }
    }
}

/// <summary>Mantiene Quartz alineado con la tabla de tareas: al arrancar y cada minuto. Si la base no responde, reintenta.</summary>
public sealed class ArranqueTareas(ProgramadorTareas programador, ILogger<ArranqueTareas> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await programador.SincronizarAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning("No se pudieron sincronizar las tareas ({Mensaje}); se reintenta.", ex.Message);
            }
            try { await Task.Delay(TimeSpan.FromSeconds(60), ct); } catch (OperationCanceledException) { return; }
        }
    }
}

/// <summary>Registra la ejecución de una tarea y, según su clave, hace el trabajo.</summary>
public sealed class EjecutorTareas(
    CajaComunDbContext db, IProgramadorTareas programador, ICacheDatos cache, TimeProvider reloj, ILogger<EjecutorTareas> log)
    : IEjecutorTareas
{
    public const string CalculoInteres = "calculo_interes";

    private sealed class FilaInteres
    {
        public long PrestamosProcesados { get; set; }
        public decimal InteresTotal { get; set; }
    }

    public async Task<EjecucionDto> EjecutarAsync(int tareaId, CancellationToken ct = default)
    {
        var tarea = await db.Tareas.FirstOrDefaultAsync(t => t.Id == tareaId, ct)
            ?? throw ErrorNegocio.NoEncontrado(Codigos.TareaNoEncontrada, "La tarea no existe.");

        // La ejecución se guarda al iniciar: si algo falla queda el registro con su estado.
        var ejecucion = new TareaEjecucion { TareaId = tarea.Id, Inicio = reloj.GetUtcNow().UtcDateTime, Estado = EstadoEjecucion.EnCurso };
        db.TareaEjecuciones.Add(ejecucion);
        await db.SaveChangesAsync(ct);

        try
        {
            ejecucion.Resumen = tarea.Clave switch
            {
                CalculoInteres => await CalcularInteresAsync(ct),
                _ => throw new InvalidOperationException($"La tarea {tarea.Clave} no tiene una acción asociada.")
            };
            ejecucion.Estado = EstadoEjecucion.Exito;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Falló la tarea {Clave}", tarea.Clave);
            ejecucion.Estado = EstadoEjecucion.Error;
            ejecucion.Resumen = Truncar($"Error: {ex.Message}", 500);
        }

        ejecucion.Fin = reloj.GetUtcNow().UtcDateTime;
        tarea.UltimaEjecucion = ejecucion.Fin;
        tarea.ProximaEjecucion = await programador.ProximaEjecucionUtcAsync(tarea.Id, ct);
        await db.SaveChangesAsync(ct);
        return new EjecucionDto(ejecucion.Id, ejecucion.Inicio, ejecucion.Fin, ejecucion.Estado, ejecucion.Resumen);
    }

    /// <summary>
    /// Devenga el interés del periodo abierto de cada caja activa con sp_calcular_interes_devengado y refresca lo que está
    /// en caché (dashboard y reportes) para que muestren el nuevo devengado. El corte es la fecha de hoy dentro del
    /// primer periodo abierto de la caja, igual que las demás operaciones.
    /// </summary>
    private async Task<string> CalcularInteresAsync(CancellationToken ct)
    {
        var cajas = await db.Cajas.AsNoTracking().Where(c => c.Activa).OrderBy(c => c.Id).ToListAsync(ct);
        var hoy = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);
        var detalle = new List<string>();
        long prestamos = 0;
        decimal interes = 0;

        foreach (var caja in cajas)
        {
            var abierto = await db.Periodos.AsNoTracking()
                .Where(p => p.CajaId == caja.Id && p.Estado == EstadoPeriodo.Abierto)
                .OrderBy(p => p.FechaInicio).FirstOrDefaultAsync(ct);
            if (abierto is null)
            {
                detalle.Add($"{caja.Prefijo}: sin periodo abierto");
                continue;
            }

            var corte = ReglasPrestamo.FechaOperacion(hoy, abierto.FechaInicio, abierto.FechaFin);
            var fila = (await db.ConsultarAsync<FilaInteres>(
                "CALL sp_calcular_interes_devengado(@caja, @corte)",
                new { caja = caja.Id, corte = corte.ToDateTime(TimeOnly.MinValue) }, ct)).Single();
            prestamos += fila.PrestamosProcesados;
            interes += fila.InteresTotal;
            detalle.Add($"{caja.Prefijo} {Periodicidad.Referencia(caja.FrecuenciaNomina, abierto.Numero, abierto.Anio)}: {fila.PrestamosProcesados} préstamos, {fila.InteresTotal:N2}");
            await cache.InvalidarAsync(caja.Id);
        }

        return Truncar($"{cajas.Count} cajas, {prestamos} préstamos, interés devengado {interes:N2} MXN. " + string.Join("; ", detalle), 500);
    }

    private static string Truncar(string texto, int maximo) => texto.Length <= maximo ? texto : texto[..maximo];
}

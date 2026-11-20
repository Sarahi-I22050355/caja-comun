using CajaComun.Application.Comun;
using CajaComun.Application.Tareas;
using CajaComun.Application.Trabajos;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Infrastructure.Persistence;
using CajaComun.Infrastructure.Servicios;
using CajaComun.Infrastructure.Tareas;
using CajaComun.Infrastructure.Trabajos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quartz;

namespace CajaComun.Tests.Integracion;

public sealed class ProgramadorFalso : IProgramadorTareas
{
    public List<int> Reprogramadas { get; } = [];
    public Task ReprogramarAsync(int tareaId, CancellationToken ct = default) { Reprogramadas.Add(tareaId); return Task.CompletedTask; }
    public Task<DateTime?> ProximaEjecucionUtcAsync(int tareaId, CancellationToken ct = default) => Task.FromResult<DateTime?>(null);
}

[Collection(ColeccionMariaDb.Nombre)]
[Trait("Category", Categorias.Integracion)]
public class TareasIntegracionTests(MariaDbFixture fx)
{
    private async Task<TareaProgramada> TareaAsync(string clave, FrecuenciaTarea frecuencia = FrecuenciaTarea.Quincenal,
        string hora = "02:00", bool activa = true)
    {
        await using var db = fx.Crear();
        var t = new TareaProgramada { Clave = clave, Nombre = "Tarea de prueba", Frecuencia = frecuencia, Hora = TimeOnly.Parse(hora), Activa = activa };
        db.Tareas.Add(t);
        await db.SaveChangesAsync();
        return t;
    }

    private async Task<TareaProgramada> TareaDeInteresAsync()
    {
        await using var db = fx.Crear();
        // La clave del cálculo de interés es única y la comparten todas las pruebas de esta clase.
        return await db.Tareas.FirstOrDefaultAsync(t => t.Clave == EjecutorTareas.CalculoInteres)
            ?? await TareaAsync(EjecutorTareas.CalculoInteres);
    }

    private EjecutorTareas Ejecutor(CajaComunDbContext db, ICacheDatos? cache = null) =>
        new(db, new ProgramadorFalso(), cache ?? new SinCache(), TimeProvider.System, NullLogger<EjecutorTareas>.Instance);

    // ---------- Cálculo de interés ----------

    [Fact]
    public async Task El_calculo_de_interes_devenga_el_periodo_abierto_y_es_idempotente()
    {
        var tarea = await TareaDeInteresAsync();
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 20_000m);
        var prestamo = await e.PrestamoVigenteAsync(a, 12_000m, 6);

        EjecucionDto primera;
        await using (var db = fx.Crear()) primera = await Ejecutor(db).EjecutarAsync(tarea.Id);
        Assert.Equal(EstadoEjecucion.Exito, primera.Estado);
        Assert.NotNull(primera.Fin);
        Assert.Contains("préstamos", primera.Resumen);

        // Hoy cae fuera del periodo: el corte es el límite del primer abierto (Q1-2024), con el saldo del desembolso previo.
        await using var v = fx.Crear();
        var fila = await v.InteresesDevengados.AsNoTracking().SingleAsync(d => d.PrestamoId == prestamo.Id);
        Assert.Equal(e.Periodos[0].Id, fila.PeriodoId);
        Assert.Equal(12_000m, fila.SaldoInsoluto);
        Assert.Equal(90m, fila.Interes);                                       // 12,000 x 1.5% / 2
        Assert.Equal(e.Periodos[0].FechaFin, fila.FechaCorte);

        await using (var db = fx.Crear()) await Ejecutor(db).EjecutarAsync(tarea.Id);
        await using var v2 = fx.Crear();
        Assert.Equal(1, await v2.InteresesDevengados.CountAsync(d => d.PrestamoId == prestamo.Id));
        var guardada = await v2.Tareas.AsNoTracking().SingleAsync(t => t.Id == tarea.Id);
        Assert.NotNull(guardada.UltimaEjecucion);
        Assert.True(await v2.TareaEjecuciones.CountAsync(x => x.TareaId == tarea.Id) >= 2);
    }

    [Fact]
    public async Task Al_aplicar_el_periodo_el_siguiente_calculo_toma_el_saldo_nuevo()
    {
        var tarea = await TareaDeInteresAsync();
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 20_000m);
        var prestamo = await e.PrestamoVigenteAsync(a, 12_000m, 6);
        await e.AplicarAnio2024Async(0, 0);                                    // cobra la cuota 1: capital 2,000

        await using (var db = fx.Crear()) await Ejecutor(db).EjecutarAsync(tarea.Id);
        await using var v = fx.Crear();
        var fila = await v.InteresesDevengados.AsNoTracking().SingleAsync(d => d.PrestamoId == prestamo.Id);
        Assert.Equal(e.Periodos[1].Id, fila.PeriodoId);                        // ahora el periodo abierto es Q2
        Assert.Equal(10_000m, fila.SaldoInsoluto);
        Assert.Equal(75m, fila.Interes);
    }

    [Fact]
    public async Task La_ejecucion_refresca_el_cache_de_las_cajas_con_periodo_abierto()
    {
        var tarea = await TareaDeInteresAsync();
        var e = await Entorno.CrearAsync(fx);
        await e.AhorradorAsync(1_000m);
        var espia = new CacheEspia();

        await using var db = fx.Crear();
        await Ejecutor(db, espia).EjecutarAsync(tarea.Id);
        Assert.Contains(e.CajaId, espia.Invalidaciones);
    }

    [Fact]
    public async Task Una_tarea_sin_accion_o_inexistente_se_registra_como_error_o_falla()
    {
        var sinAccion = await TareaAsync($"sin-accion-{Guid.NewGuid():N}"[..30]);
        await using (var db = fx.Crear())
        {
            var r = await Ejecutor(db).EjecutarAsync(sinAccion.Id);
            Assert.Equal(EstadoEjecucion.Error, r.Estado);
            Assert.Contains("no tiene una acción", r.Resumen);
        }
        await using var db2 = fx.Crear();
        var error = await Assert.ThrowsAsync<ErrorNegocio>(() => Ejecutor(db2).EjecutarAsync(int.MaxValue));
        Assert.Equal(Codigos.TareaNoEncontrada, error.Codigo);
    }

    // ---------- Servicio ----------

    private TareaService Servicio(CajaComunDbContext db, ProgramadorFalso programador, ColaTrabajos cola) =>
        new(db, programador, cola, new UsuarioPrueba(7));

    [Fact]
    public async Task Actualizar_guarda_la_configuracion_y_pide_reprogramar_en_caliente()
    {
        var t = await TareaAsync($"edit-{Guid.NewGuid():N}"[..20]);
        var programador = new ProgramadorFalso();
        var cola = new ColaTrabajos(new AlmacenTrabajosMemoria(), TimeProvider.System);

        await using var db = fx.Crear();
        var r = await Servicio(db, programador, cola).ActualizarAsync(t.Id, new ActualizarTareaRequest(FrecuenciaTarea.Diaria, new TimeOnly(3, 30, 45), false));
        Assert.Equal((FrecuenciaTarea.Diaria, new TimeOnly(3, 30), false), (r.Frecuencia, r.Hora, r.Activa));   // sin segundos
        Assert.Equal([t.Id], programador.Reprogramadas);
        await using var v = fx.Crear();
        Assert.False((await v.Tareas.AsNoTracking().SingleAsync(x => x.Id == t.Id)).Activa);

        await using var db2 = fx.Crear();
        var error = await Assert.ThrowsAsync<ErrorNegocio>(() => Servicio(db2, programador, cola)
            .ActualizarAsync(int.MaxValue, new ActualizarTareaRequest(FrecuenciaTarea.Diaria, new TimeOnly(1, 0), true)));
        Assert.Equal(Codigos.TareaNoEncontrada, error.Codigo);
    }

    [Fact]
    public async Task Ejecutar_ahora_encola_un_trabajo_y_el_historial_se_pagina()
    {
        var t = await TareaAsync($"hist-{Guid.NewGuid():N}"[..20]);
        var almacen = new AlmacenTrabajosMemoria();
        var cola = new ColaTrabajos(almacen, TimeProvider.System);
        await using (var db = fx.Crear())
        {
            var trabajo = await Servicio(db, new ProgramadorFalso(), cola).EjecutarAhoraAsync(t.Id);
            Assert.Equal((EstadoTrabajo.Pendiente, "TAREA"), (trabajo.Estado, trabajo.Tipo));
            Assert.NotNull(await almacen.ObtenerAsync(trabajo.Id));
        }

        await using (var db = fx.Crear())
        {
            for (var i = 0; i < 5; i++)
                db.TareaEjecuciones.Add(new TareaEjecucion { TareaId = t.Id, Inicio = DateTime.UtcNow.AddMinutes(-i), Fin = DateTime.UtcNow, Estado = EstadoEjecucion.Exito, Resumen = $"corrida {i}" });
            await db.SaveChangesAsync();
        }
        await using var v = fx.Crear();
        var pagina = await Servicio(v, new ProgramadorFalso(), cola).HistorialAsync(t.Id, new ConsultaPaginada { PageSize = 2, Page = 2 });
        Assert.Equal((5, 2, 3), (pagina.Total, pagina.Items.Count, pagina.TotalPages));
        Assert.Equal("corrida 2", pagina.Items[0].Resumen);                    // del más reciente al más antiguo
    }

    [Fact]
    public async Task El_listado_trae_el_ultimo_estado_de_cada_tarea()
    {
        var t = await TareaAsync($"lista-{Guid.NewGuid():N}"[..20]);
        await using (var db = fx.Crear())
        {
            db.TareaEjecuciones.Add(new TareaEjecucion { TareaId = t.Id, Inicio = DateTime.UtcNow, Estado = EstadoEjecucion.Error, Resumen = "falló" });
            await db.SaveChangesAsync();
            db.TareaEjecuciones.Add(new TareaEjecucion { TareaId = t.Id, Inicio = DateTime.UtcNow, Estado = EstadoEjecucion.Exito, Resumen = "bien" });
            await db.SaveChangesAsync();
        }
        await using var v = fx.Crear();
        var servicio = Servicio(v, new ProgramadorFalso(), new ColaTrabajos(new AlmacenTrabajosMemoria(), TimeProvider.System));
        var tarea = (await servicio.ListarAsync()).Single(x => x.Id == t.Id);
        Assert.Equal((EstadoEjecucion.Exito, "bien"), (tarea.UltimoEstado, tarea.UltimoResumen));
        Assert.Equal(t.Id, (await servicio.ObtenerAsync(t.Id)).Id);
    }

    // ---------- Quartz ----------

    private sealed class Quartzs : IAsyncDisposable
    {
        public ServiceProvider Servicios { get; }
        public ProgramadorTareas Programador { get; }
        public ISchedulerFactory Fabrica { get; }

        public Quartzs(MariaDbFixture fx)
        {
            // Quartz guarda un registro global: si lo dejó un host ya destruido, las pruebas siguientes fallan.
            Quartz.Logging.LogContext.SetCurrentLogProvider(Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance);
            var s = new ServiceCollection();
            s.AddDbContext<CajaComunDbContext>(o => o.UseMySql(fx.Cadena, new MariaDbServerVersion(new Version(11, 4, 0))));
            s.AddQuartz(q => q.SchedulerName = "prueba-" + Guid.NewGuid().ToString("N"));
            s.AddLogging();
            s.AddSingleton<ProgramadorTareas>();
            Servicios = s.BuildServiceProvider();
            Programador = Servicios.GetRequiredService<ProgramadorTareas>();
            Fabrica = Servicios.GetRequiredService<ISchedulerFactory>();
        }

        public async Task<ICronTrigger?> TriggerAsync(int id) =>
            await (await Fabrica.GetScheduler()).GetTrigger(new TriggerKey($"tarea-{id}", "tareas")) as ICronTrigger;

        public async ValueTask DisposeAsync()
        {
            await (await Fabrica.GetScheduler()).Shutdown(false);
            await Servicios.DisposeAsync();
        }
    }

    [Fact]
    public async Task Programar_calcula_la_proxima_ejecucion_en_hora_de_mexico_y_la_guarda()
    {
        var t = await TareaAsync($"quartz-{Guid.NewGuid():N}"[..20], FrecuenciaTarea.Diaria, "03:30");
        await using var q = new Quartzs(fx);

        await q.Programador.ReprogramarAsync(t.Id);
        var proxima = await q.Programador.ProximaEjecucionUtcAsync(t.Id);
        Assert.NotNull(proxima);
        Assert.Equal(new TimeOnly(9, 30), TimeOnly.FromDateTime(proxima!.Value));       // 03:30 en UTC-6
        Assert.True(proxima > DateTime.UtcNow);

        await using var v = fx.Crear();
        Assert.Equal(proxima, DateTime.SpecifyKind((await v.Tareas.AsNoTracking().SingleAsync(x => x.Id == t.Id)).ProximaEjecucion!.Value, DateTimeKind.Utc));
        Assert.Equal("0 30 3 * * ?", (await q.TriggerAsync(t.Id))!.CronExpressionString);
    }

    [Fact]
    public async Task Cambiar_frecuencia_hora_o_interruptor_reprograma_sin_reiniciar()
    {
        var t = await TareaAsync($"cambio-{Guid.NewGuid():N}"[..20], FrecuenciaTarea.Diaria, "03:30");
        await using var q = new Quartzs(fx);
        await q.Programador.ReprogramarAsync(t.Id);

        await using (var db = fx.Crear())
        {
            var fila = await db.Tareas.SingleAsync(x => x.Id == t.Id);
            fila.Frecuencia = FrecuenciaTarea.Mensual;
            fila.Hora = new TimeOnly(6, 0);
            await db.SaveChangesAsync();
        }
        await q.Programador.ReprogramarAsync(t.Id);
        var mensual = await q.Programador.ProximaEjecucionUtcAsync(t.Id);
        Assert.Equal(1, mensual!.Value.AddHours(-6).Day);                                 // día 1 a las 06:00 de México
        Assert.Equal(new TimeOnly(12, 0), TimeOnly.FromDateTime(mensual.Value));
        Assert.Equal("0 0 6 1 * ?", (await q.TriggerAsync(t.Id))!.CronExpressionString);

        await using (var db = fx.Crear())
        {
            (await db.Tareas.SingleAsync(x => x.Id == t.Id)).Activa = false;
            await db.SaveChangesAsync();
        }
        await q.Programador.ReprogramarAsync(t.Id);
        Assert.Null(await q.Programador.ProximaEjecucionUtcAsync(t.Id));
        Assert.Null(await q.TriggerAsync(t.Id));
        await using var v = fx.Crear();
        Assert.Null((await v.Tareas.AsNoTracking().SingleAsync(x => x.Id == t.Id)).ProximaEjecucion);
    }

    [Fact]
    public async Task La_quincenal_corre_los_dias_1_y_16()
    {
        var t = await TareaAsync($"quin-{Guid.NewGuid():N}"[..20], FrecuenciaTarea.Quincenal, "02:00");
        await using var q = new Quartzs(fx);
        await q.Programador.ReprogramarAsync(t.Id);
        var trigger = (await q.TriggerAsync(t.Id))!;

        var dias = new List<int>();
        var desde = DateTimeOffset.UtcNow;
        for (var i = 0; i < 4; i++)
        {
            desde = trigger.GetFireTimeAfter(desde)!.Value;
            dias.Add(desde.ToOffset(TimeSpan.FromHours(-6)).Day);
        }
        Assert.All(dias, d => Assert.Contains(d, new[] { 1, 16 }));
        Assert.Equal(2, dias.Distinct().Count());
    }

    [Fact]
    public async Task Sincronizar_alcanza_los_cambios_hechos_fuera_de_la_api()
    {
        var t = await TareaAsync($"sync-{Guid.NewGuid():N}"[..20], FrecuenciaTarea.Diaria, "04:00");
        await using var q = new Quartzs(fx);

        await q.Programador.SincronizarAsync(CancellationToken.None);               // tarea nueva
        Assert.Equal("0 0 4 * * ?", (await q.TriggerAsync(t.Id))!.CronExpressionString);

        await using (var db = fx.Crear())                                           // alguien la edita directo en la base
            await db.EjecutarAsync("UPDATE tarea_programada SET frecuencia = 'MENSUAL', hora = '05:15:00' WHERE id = @id", new { id = t.Id });
        await q.Programador.SincronizarAsync(CancellationToken.None);
        Assert.Equal("0 15 5 1 * ?", (await q.TriggerAsync(t.Id))!.CronExpressionString);

        await using (var db = fx.Crear())                                           // la base se reinició: se perdió la próxima ejecución
            await db.EjecutarAsync("UPDATE tarea_programada SET proxima_ejecucion = NULL WHERE id = @id", new { id = t.Id });
        await q.Programador.SincronizarAsync(CancellationToken.None);
        await using var v = fx.Crear();
        Assert.NotNull((await v.Tareas.AsNoTracking().SingleAsync(x => x.Id == t.Id)).ProximaEjecucion);
    }
}

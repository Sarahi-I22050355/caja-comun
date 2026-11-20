using CajaComun.Application.AhorroVoluntario;
using CajaComun.Application.Comun;
using CajaComun.Application.Prestamos;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Infrastructure.Persistence;
using CajaComun.Infrastructure.Servicios;
using Microsoft.EntityFrameworkCore;

namespace CajaComun.Tests.Integracion;

[Collection(ColeccionMariaDb.Nombre)]
[Trait("Category", Categorias.Integracion)]
public class PrestamosIntegracionTests(MariaDbFixture fx)
{
    private static async Task<ErrorNegocio> Error(Func<Task> accion) => await Assert.ThrowsAsync<ErrorNegocio>(accion);

    private static SolicitudPrestamoRequest Solicitud(Ahorrador a, decimal monto, int plazo = 12, int? aval = null) =>
        new(a.Id, monto, plazo, aval);

    private async Task<T> ConServicio<T>(Entorno e, Func<PrestamoService, Task<T>> accion, int? usuarioId = null)
    {
        await using var db = e.Db();
        return await accion(e.Prestamos(db, usuarioId));
    }

    [Fact]
    public async Task La_simulacion_evalua_la_garantia_y_calcula_la_tabla_sin_guardar_nada()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 20_000m);       // límite: 16,000.00

        var dentro = await ConServicio(e, s => s.SimularAsync(Solicitud(a, 10_000m, 8)));
        Assert.True(dentro.Evaluacion.Aprobada);
        Assert.Equal(16_000m, dentro.Evaluacion.Limite);
        Assert.Equal(GarantiaPrestamo.Ahorro80, dentro.Evaluacion.Garantia);
        Assert.Equal(8, dentro.Amortizacion.Count);
        Assert.Equal("Q2-2024", dentro.Amortizacion[0].Periodo);        // la primera cuota cae en el periodo siguiente al abierto
        Assert.Equal(1_250m + 75m, dentro.PrimeraCuota);
        Assert.Equal(dentro.Amortizacion.Sum(c => c.Interes), dentro.TotalIntereses);

        var fuera = await ConServicio(e, s => s.SimularAsync(Solicitud(a, 21_000m, 8)));
        Assert.False(fuera.Evaluacion.Aprobada);
        Assert.Equal("AVAL_REQUERIDO", fuera.Evaluacion.Motivo);
        Assert.Equal(5_000m, fuera.Evaluacion.ExcedenteAval);

        await using var db = e.Db();
        Assert.Equal(0, await db.Prestamos.CountAsync(p => p.CajaId == e.CajaId));
    }

    [Fact]
    public async Task Un_prestamo_que_cruza_el_ciclo_se_simula_con_periodos_proyectados()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 100_000m);
        await using (var db = e.Db())     // la caja solo conserva los periodos de 2024
            await db.EjecutarAsync("DELETE FROM periodo WHERE caja_id = @c AND anio = 2025", new { c = e.CajaId });

        var sim = await ConServicio(e, s => s.SimularAsync(Solicitud(a, 30_000m, 36)));
        Assert.Equal(36, sim.Amortizacion.Count);
        Assert.Equal("Q24-2024", sim.Amortizacion[22].Periodo);
        Assert.Equal("Q1-2025", sim.Amortizacion[23].Periodo);
        Assert.Equal("Q13-2025", sim.Amortizacion[35].Periodo);
        Assert.All(sim.Amortizacion.Take(23), c => Assert.NotNull(c.PeriodoId));
        Assert.All(sim.Amortizacion.Skip(23), c => Assert.Null(c.PeriodoId));      // proyectados: no se creó nada

        await using var db2 = e.Db();
        Assert.Equal(0, await db2.Periodos.CountAsync(p => p.CajaId == e.CajaId && p.Anio == 2025));
    }

    [Fact]
    public async Task Al_solicitar_un_prestamo_que_cruza_el_ciclo_se_generan_los_periodos_que_faltan()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 100_000m);
        await using (var db = e.Db())
            await db.EjecutarAsync("DELETE FROM periodo WHERE caja_id = @c AND anio = 2025", new { c = e.CajaId });

        var creado = await ConServicio(e, s => s.CrearAsync(Solicitud(a, 30_000m, 30)));
        Assert.Equal("Q1-2025", creado.Amortizacion[23].Periodo);
        Assert.All(creado.Amortizacion, c => Assert.NotNull(c.PeriodoId));

        await using var db2 = e.Db();
        Assert.Equal(7, await db2.Periodos.CountAsync(p => p.CajaId == e.CajaId && p.Anio == 2025));   // Q1..Q7
        Assert.All(await db2.Periodos.Where(p => p.CajaId == e.CajaId && p.Anio == 2025).ToListAsync(),
            p => Assert.Equal(EstadoPeriodo.Abierto, p.Estado));
    }

    [Fact]
    public async Task La_solicitud_queda_pendiente_con_folio_consecutivo_y_tabla_proyectada()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 20_000m);

        var creado = await ConServicio(e, s => s.CrearAsync(Solicitud(a, 12_000m, 6)));
        var p = creado.Prestamo;
        Assert.Equal(EstadoPrestamo.Pendiente, p.Estado);
        Assert.StartsWith("FOL-2024-", p.Folio);
        Assert.Equal(1.5m, p.TasaMensual);
        Assert.Equal(GarantiaPrestamo.Ahorro80, p.Garantia);
        Assert.Equal(6, creado.Amortizacion.Count);
        Assert.All(creado.Amortizacion, c => Assert.Equal(EstadoAmortizacion.Proyectado, c.Estado));
        Assert.Equal(0m, p.SaldoVigente);                                // todavía no se desembolsa
        Assert.Null(creado.CuotaActual);

        var segunda = await Error(() => ConServicio(e, s => s.CrearAsync(Solicitud(a, 1_000m, 4))));
        Assert.Equal(Codigos.PrestamoPendienteExistente, segunda.Codigo);
    }

    [Fact]
    public async Task Seis_solicitudes_simultaneas_obtienen_folios_distintos()
    {
        var e = await Entorno.CrearAsync(fx);
        var ahorradores = new List<Ahorrador>();
        for (var i = 0; i < 6; i++) ahorradores.Add(await e.AhorradorAsync(1_000m, ahorro: 20_000m));

        var creados = await Task.WhenAll(ahorradores.Select(a => ConServicio(e, s => s.CrearAsync(Solicitud(a, 5_000m, 4)))));
        var folios = creados.Select(c => c.Prestamo.Folio).ToList();
        Assert.Equal(6, folios.Distinct().Count());
        var numeros = folios.Select(f => int.Parse(f[^3..])).OrderBy(n => n).ToList();
        Assert.Equal(Enumerable.Range(numeros[0], 6), numeros);
    }

    [Fact]
    public async Task Sobre_el_80_por_ciento_exige_aval_que_cubra_el_excedente()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 10_000m);       // límite 8,000: pedir 10,000 deja 2,000 por avalar
        var fuerte = await e.AhorradorAsync(1_000m, ahorro: 10_000m);
        var debil = await e.AhorradorAsync(1_000m, ahorro: 1_000m);    // disponible 800

        Assert.Equal(Codigos.AvalRequerido, (await Error(() => ConServicio(e, s => s.CrearAsync(Solicitud(a, 10_000m))))).Codigo);
        Assert.Equal(Codigos.AvalInvalido, (await Error(() => ConServicio(e, s => s.CrearAsync(Solicitud(a, 10_000m, aval: a.Id))))).Codigo);
        Assert.Equal(Codigos.AvalInsuficiente, (await Error(() => ConServicio(e, s => s.CrearAsync(Solicitud(a, 10_000m, aval: debil.Id))))).Codigo);

        var ok = await ConServicio(e, s => s.CrearAsync(Solicitud(a, 10_000m, aval: fuerte.Id)));
        Assert.Equal(GarantiaPrestamo.PagareAval, ok.Prestamo.Garantia);
        Assert.Equal(2_000m, ok.Prestamo.ExcedenteAval);
        Assert.Equal(fuerte.Id, ok.Prestamo.Aval!.AhorradorId);
    }

    [Fact]
    public async Task El_aval_de_otra_caja_o_dado_de_baja_no_vale()
    {
        var e = await Entorno.CrearAsync(fx);
        var otra = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 10_000m);
        var ajeno = await otra.AhorradorAsync(1_000m, ahorro: 50_000m);
        var baja = await e.AhorradorAsync(1_000m, ahorro: 50_000m);
        await using (var db = e.Db()) await db.EjecutarAsync("UPDATE ahorrador SET estado = 'BAJA', fecha_baja = '2024-01-02' WHERE id = @id", new { id = baja.Id });

        Assert.Equal(Codigos.AvalInvalido, (await Error(() => ConServicio(e, s => s.CrearAsync(Solicitud(a, 10_000m, aval: ajeno.Id))))).Codigo);
        Assert.Equal(Codigos.AvalInvalido, (await Error(() => ConServicio(e, s => s.CrearAsync(Solicitud(a, 10_000m, aval: baja.Id))))).Codigo);
    }

    [Fact]
    public async Task El_ahorro_de_un_aval_no_se_compromete_dos_veces()
    {
        var e = await Entorno.CrearAsync(fx);
        var aval = await e.AhorradorAsync(1_000m, ahorro: 10_000m);    // puede respaldar hasta 8,000
        var uno = await e.AhorradorAsync(1_000m, ahorro: 10_000m);
        var dos = await e.AhorradorAsync(1_000m, ahorro: 10_000m);

        await ConServicio(e, s => s.CrearAsync(Solicitud(uno, 13_000m, aval: aval.Id)));        // excedente 5,000

        // Le quedan 3,000: pedir 4,000 por encima de su límite ya no cabe, pero 3,000 justos sí.
        var excede = await ConServicio(e, s => s.SimularAsync(Solicitud(dos, 12_000m, aval: aval.Id)));
        Assert.Equal(3_000m, excede.Evaluacion.Aval!.Disponible);
        Assert.False(excede.Evaluacion.Aprobada);
        Assert.Equal(Codigos.AvalInsuficiente, excede.Evaluacion.Motivo);
        Assert.Equal(Codigos.AvalInsuficiente,
            (await Error(() => ConServicio(e, s => s.CrearAsync(Solicitud(dos, 12_000m, aval: aval.Id))))).Codigo);

        var justo = await ConServicio(e, s => s.CrearAsync(Solicitud(dos, 11_000m, aval: aval.Id)));
        Assert.Equal(3_000m, justo.Prestamo.ExcedenteAval);

        // Rechazar la primera libera lo que respaldaba.
        var primero = (await ConServicio(e, s => s.ListarAsync(new ConsultaPrestamos { AhorradorId = uno.Id }))).Items.Single().Id;
        await ConServicio(e, s => s.RechazarAsync(primero));
        var tercero = await e.AhorradorAsync(1_000m, ahorro: 10_000m);
        var libre = await ConServicio(e, s => s.SimularAsync(Solicitud(tercero, 12_000m, aval: aval.Id)));
        Assert.Equal(5_000m, libre.Evaluacion.Aval!.Disponible);          // 8,000 - 3,000 de la segunda
    }

    [Fact]
    public async Task Dos_solicitudes_simultaneas_del_mismo_ahorrador_dejan_una_sola_pendiente()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 20_000m);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try { return (Ok: await ConServicio(e, s => s.CrearAsync(Solicitud(a, 3_000m, 4))), Error: (ErrorNegocio?)null); }
            catch (ErrorNegocio ex) { return (Ok: null, Error: ex); }
        }));

        Assert.Single(resultados, r => r.Ok is not null);
        Assert.All(resultados.Where(r => r.Error is not null), r => Assert.Equal(Codigos.PrestamoPendienteExistente, r.Error!.Codigo));
        await using var db = e.Db();
        Assert.Equal(1, await db.Prestamos.CountAsync(p => p.AhorradorId == a.Id));
    }

    [Fact]
    public async Task Dos_solicitudes_simultaneas_no_comprometen_dos_veces_el_mismo_aval()
    {
        var e = await Entorno.CrearAsync(fx);
        var aval = await e.AhorradorAsync(1_000m, ahorro: 10_000m);    // respalda hasta 8,000
        var uno = await e.AhorradorAsync(1_000m, ahorro: 10_000m);
        var dos = await e.AhorradorAsync(1_000m, ahorro: 10_000m);

        // Cada una pide 13,000 (excedente 5,000): juntas serían 10,000 y el aval solo cubre 8,000.
        var resultados = await Task.WhenAll(new[] { uno, dos }.Select(async a =>
        {
            try { return (Ok: await ConServicio(e, s => s.CrearAsync(Solicitud(a, 13_000m, 6, aval.Id))), Error: (ErrorNegocio?)null); }
            catch (ErrorNegocio ex) { return (Ok: null, Error: ex); }
        }));

        Assert.Single(resultados, r => r.Ok is not null);
        Assert.Equal(Codigos.AvalInsuficiente, resultados.Single(r => r.Error is not null).Error!.Codigo);
        await using var db = e.Db();
        Assert.Equal(5_000m, await db.Prestamos.Where(p => p.AvalAhorradorId == aval.Id).SumAsync(p => p.ExcedenteAval));
    }

    [Fact]
    public async Task Autorizar_desembolsa_programa_las_cuotas_y_deja_el_saldo_vigente()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 20_000m);
        var creado = await ConServicio(e, s => s.CrearAsync(Solicitud(a, 12_000m, 6)));

        var autorizado = await ConServicio(e, s => s.AutorizarAsync(creado.Prestamo.Id));
        var p = autorizado.Prestamo;
        Assert.Equal(EstadoPrestamo.Vigente, p.Estado);
        Assert.Equal(12_000m, p.SaldoVigente);
        Assert.Equal("Administrador de prueba", p.AutorizadoPor);
        Assert.NotNull(p.FechaAutorizacion);
        Assert.All(autorizado.Amortizacion, c => Assert.Equal(EstadoAmortizacion.Programado, c.Estado));
        Assert.Equal("Q2-2024", autorizado.Amortizacion[0].Periodo);
        Assert.Equal(1, autorizado.CuotaActual);

        await using var db = e.Db();
        var desembolso = await db.Movimientos.SingleAsync(m => m.PrestamoId == p.Id);
        Assert.Equal(TipoMovimiento.DesembolsoPrestamo, desembolso.Tipo);
        Assert.Equal(12_000m, desembolso.Monto);
        Assert.Equal(e.Periodos[0].FechaFin, desembolso.Fecha);          // fecha de pago del primer periodo abierto
        Assert.Equal(p.Folio, desembolso.Referencia);
        Assert.Equal(6, await db.Amortizaciones.CountAsync(x => x.PrestamoId == p.Id));   // la tabla proyectada se reemplazó
    }

    [Fact]
    public async Task Un_prestamo_se_autoriza_una_sola_vez_aunque_lleguen_varias_peticiones()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 20_000m);
        var id = (await ConServicio(e, s => s.CrearAsync(Solicitud(a, 8_000m, 4)))).Prestamo.Id;

        var resultados = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try { return (Ok: await ConServicio(e, s => s.AutorizarAsync(id)), Error: (ErrorNegocio?)null); }
            catch (ErrorNegocio ex) { return (Ok: null, Error: ex); }
        }));

        Assert.Single(resultados, r => r.Ok is not null);
        Assert.All(resultados.Where(r => r.Error is not null), r => Assert.Equal(Codigos.PrestamoNoPendiente, r.Error!.Codigo));
        await using var db = e.Db();
        Assert.Equal(1, await db.Movimientos.CountAsync(m => m.PrestamoId == id && m.Tipo == TipoMovimiento.DesembolsoPrestamo));
        Assert.Equal(4, await db.Amortizaciones.CountAsync(x => x.PrestamoId == id));
    }

    [Fact]
    public async Task Al_autorizar_se_revisa_otra_vez_la_garantia()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 20_000m);
        var id = (await ConServicio(e, s => s.CrearAsync(Solicitud(a, 15_000m, 6)))).Prestamo.Id;

        // Entre la solicitud y la autorización el ahorro baja (por ejemplo, una liquidación): ya no alcanza el 80%.
        await using (var db = e.Db())
            await db.EjecutarAsync("UPDATE movimiento SET monto = 10000 WHERE ahorrador_id = @a", new { a = a.Id });

        var error = await Error(() => ConServicio(e, s => s.AutorizarAsync(id)));
        Assert.Equal(Codigos.AvalRequerido, error.Codigo);
        await using var db2 = e.Db();
        Assert.Equal(EstadoPrestamo.Pendiente, (await db2.Prestamos.FindAsync(id))!.Estado);     // la transacción se revirtió
        Assert.Equal(0, await db2.Movimientos.CountAsync(m => m.PrestamoId == id));
    }

    [Fact]
    public async Task Rechazar_deja_constancia_y_quita_la_tabla_proyectada()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 20_000m);
        var id = (await ConServicio(e, s => s.CrearAsync(Solicitud(a, 5_000m, 4)))).Prestamo.Id;

        var rechazado = await ConServicio(e, s => s.RechazarAsync(id));
        Assert.Equal(EstadoPrestamo.Rechazado, rechazado.Prestamo.Estado);
        Assert.NotNull(rechazado.Prestamo.AutorizadoPor);
        Assert.Empty(rechazado.Amortizacion);

        Assert.Equal(Codigos.PrestamoNoPendiente, (await Error(() => ConServicio(e, s => s.AutorizarAsync(id)))).Codigo);
        // Y el ahorrador puede volver a solicitar.
        await ConServicio(e, s => s.CrearAsync(Solicitud(a, 3_000m, 4)));
    }

    [Fact]
    public async Task Solo_un_ahorrador_activo_puede_solicitar()
    {
        var e = await Entorno.CrearAsync(fx);
        var baja = await e.AhorradorAsync(1_000m, ahorro: 20_000m);
        var licencia = await e.AhorradorAsync(1_000m, ahorro: 20_000m);
        await using (var db = e.Db())
        {
            await db.EjecutarAsync("UPDATE ahorrador SET estado = 'BAJA', fecha_baja = '2024-01-02' WHERE id = @id", new { id = baja.Id });
            await db.EjecutarAsync("UPDATE ahorrador SET estado = 'LICENCIA' WHERE id = @id", new { id = licencia.Id });
        }

        Assert.Equal(Codigos.AhorradorEnBaja, (await Error(() => ConServicio(e, s => s.CrearAsync(Solicitud(baja, 1_000m, 4))))).Codigo);
        Assert.Equal(Codigos.AhorradorNoActivo, (await Error(() => ConServicio(e, s => s.CrearAsync(Solicitud(licencia, 1_000m, 4))))).Codigo);
    }

    [Fact]
    public async Task El_listado_y_el_resumen_salen_de_las_vistas_de_cartera()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 20_000m);
        var b = await e.AhorradorAsync(1_000m, ahorro: 20_000m);
        var vigente = await e.PrestamoVigenteAsync(a, 12_000m, 6);
        await ConServicio(e, s => s.CrearAsync(Solicitud(b, 5_000m, 4)));

        await using (var db = e.Db()) await e.Nomina(db).AplicarPeriodoAsync(e.Periodos[0].Id);

        var resumen = await ConServicio(e, s => s.ResumenAsync(e.CajaId));
        Assert.Equal((1, 1, 0, 0), (resumen.Vigentes, resumen.Pendientes, resumen.Liquidados, resumen.Rechazados));
        Assert.Equal(12_000m, resumen.CapitalColocado);
        Assert.Equal(10_000m, resumen.SaldoVigente);                     // se abonó la primera cuota de capital (2,000)
        Assert.Equal(90m, resumen.InteresCobrado);

        var lista = await ConServicio(e, s => s.ListarAsync(new ConsultaPrestamos { CajaId = e.CajaId, Estado = EstadoPrestamo.Vigente }));
        var fila = Assert.Single(lista.Items);
        Assert.Equal((vigente.Folio, 1, 5), (fila.Folio, fila.CuotasAplicadas, fila.CuotasRestantes));
        Assert.Equal(10_000m, fila.SaldoVigente);

        var porNombre = await ConServicio(e, s => s.ListarAsync(new ConsultaPrestamos { CajaId = e.CajaId, Q = b.NumEmpleado }));
        Assert.Equal(b.Id, Assert.Single(porNombre.Items).AhorradorId);
    }

    // ---------- Ahorro voluntario ----------

    [Fact]
    public async Task El_voluntario_se_activa_edita_y_desactiva_conservando_la_cuota_ordinaria()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 5_000m);
        await using var db = e.Db();
        var servicio = e.Voluntario(db);

        var activo = await servicio.ActualizarAsync(a.Id, new ActualizarVoluntarioRequest(true, 300m, null));
        Assert.True(activo.Activo);
        Assert.Equal(300m, activo.CuotaVoluntaria);
        Assert.Equal(e.Periodos[0].FechaInicio, activo.VigenteDesde);

        var editado = await servicio.ActualizarAsync(a.Id, new ActualizarVoluntarioRequest(true, 350m, null));
        Assert.Equal(350m, editado.CuotaVoluntaria);

        await using var db2 = e.Db();
        var cuotas = await db2.CuotasAhorro.Where(c => c.AhorradorId == a.Id).OrderBy(c => c.VigenteDesde).ToListAsync();
        Assert.Equal(2, cuotas.Count);                                   // la original y una sola nueva (la edición del mismo día la reemplaza)
        Assert.Equal(1_000m, cuotas[1].CuotaOrdinaria);

        var apagado = await servicio.ActualizarAsync(a.Id, new ActualizarVoluntarioRequest(false, 0m, null));
        Assert.False(apagado.Activo);
        Assert.Equal(0m, apagado.CuotaVoluntaria);
    }

    [Fact]
    public async Task El_resumen_y_el_listado_de_voluntario_filtran_por_estado()
    {
        var e = await Entorno.CrearAsync(fx);
        await e.AhorradorAsync(1_000m, voluntaria: 200m);
        await e.AhorradorAsync(1_000m, voluntaria: 300m);
        await e.AhorradorAsync(1_000m);

        await using var db = e.Db();
        var servicio = e.Voluntario(db);
        var resumen = await servicio.ResumenAsync(e.CajaId);
        Assert.Equal((2, 500m), (resumen.AhorradoresActivos, resumen.CuotaPorPeriodo));

        Assert.Equal(2, (await servicio.ListarAsync(new ConsultaVoluntario { CajaId = e.CajaId, Activo = true })).Total);
        Assert.Equal(1, (await servicio.ListarAsync(new ConsultaVoluntario { CajaId = e.CajaId, Activo = false })).Total);
        Assert.Equal(3, (await servicio.ListarAsync(new ConsultaVoluntario { CajaId = e.CajaId })).Total);
    }

    [Fact]
    public async Task El_voluntario_no_cambia_periodos_ya_aplicados_ni_a_quien_esta_de_baja()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m);
        await using (var db = e.Db()) await e.Nomina(db).AplicarPeriodoAsync(e.Periodos[0].Id);

        await using var db2 = e.Db();
        var servicio = e.Voluntario(db2);
        var error = await Error(() => servicio.ActualizarAsync(a.Id, new ActualizarVoluntarioRequest(true, 100m, e.Periodos[0].FechaInicio)));
        Assert.Equal(Codigos.CuotaFechaInvalida, error.Codigo);

        // Sin fecha rige desde el primer periodo abierto, que ahora es el segundo.
        var nuevo = await servicio.ActualizarAsync(a.Id, new ActualizarVoluntarioRequest(true, 100m, null));
        Assert.Equal(e.Periodos[1].FechaInicio, nuevo.VigenteDesde);

        await db2.EjecutarAsync("UPDATE ahorrador SET estado = 'BAJA', fecha_baja = '2024-02-01' WHERE id = @id", new { id = a.Id });
        Assert.Equal(Codigos.AhorradorEnBaja,
            (await Error(() => servicio.ActualizarAsync(a.Id, new ActualizarVoluntarioRequest(false, 0m, null)))).Codigo);
    }
}

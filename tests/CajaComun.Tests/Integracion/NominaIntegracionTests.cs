using CajaComun.Application.Comun;
using CajaComun.Application.Nomina;
using CajaComun.Domain.Enums;
using CajaComun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CajaComun.Tests.Integracion;

[Collection(ColeccionMariaDb.Nombre)]
[Trait("Category", Categorias.Integracion)]
public class NominaIntegracionTests(MariaDbFixture fx)
{
    private static async Task<ErrorNegocio> Error(Func<Task> accion) => await Assert.ThrowsAsync<ErrorNegocio>(accion);

    private async Task<AplicarPeriodoResultadoDto> AplicarAsync(Entorno e, int indice)
    {
        await using var db = e.Db();
        return await e.Nomina(db).AplicarPeriodoAsync(e.Periodos[indice].Id);
    }

    [Fact]
    public async Task Aplicar_el_periodo_genera_los_movimientos_que_calcula_la_vista_y_lo_marca_aplicado()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(ordinaria: 1_400m, ahorro: 10_000m, voluntaria: 500m);
        var b = await e.AhorradorAsync(ordinaria: 900m);
        var c = await e.AhorradorAsync(ordinaria: 1_000m, voluntaria: 300m);
        await e.IncidenciaAsync(c, 0, TipoIncidencia.SuspendeVoluntario);
        var prestamo = await e.PrestamoVigenteAsync(a, 12_000m, 6);      // cuota 1: capital 2,000.00 + interés 90.00

        var r = await AplicarAsync(e, 0);

        Assert.Equal("Q1-2024", r.Referencia);
        Assert.Equal(6, r.MovimientosGenerados);
        Assert.Equal(3_300m, r.AhorroOrdinario);
        Assert.Equal(500m, r.AhorroVoluntario);
        Assert.Equal(2_000m, r.CapitalPrestamos);
        Assert.Equal(90m, r.InteresPrestamos);
        Assert.Equal(0, r.CuotasDiferidas);

        await using var db = e.Db();
        Assert.Equal(EstadoPeriodo.Aplicado, (await db.Periodos.FindAsync(e.Periodos[0].Id))!.Estado);
        var movs = await e.MovimientosAsync(0);
        Assert.Equal(6, movs.Count);
        Assert.All(movs, m => Assert.Equal(e.Periodos[0].FechaFin, m.Fecha));
        Assert.Equal(["Q1-2024"], movs.Where(m => m.PrestamoId is null).Select(m => m.Referencia).Distinct());
        Assert.All(movs.Where(m => m.PrestamoId is not null), m => Assert.Equal(prestamo.Folio, m.Referencia));
        Assert.DoesNotContain(movs, m => m.AhorradorId == c.Id && m.Tipo == TipoMovimiento.AhorroVoluntario);
        Assert.Equal(EstadoAmortizacion.Aplicado,
            (await db.Amortizaciones.SingleAsync(x => x.PrestamoId == prestamo.Id && x.Numero == 1)).Estado);

        // Lo aplicado coincide, ahorrador por ahorrador, con lo que la vista calculaba para el periodo.
        var diferencias = await db.EscalarAsync<int>(
            """
            SELECT CAST(COUNT(*) AS SIGNED)
              FROM v_descuento_periodo d
              LEFT JOIN (SELECT ahorrador_id, SUM(monto) AS m FROM movimiento
                          WHERE periodo_id = @periodo GROUP BY ahorrador_id) mv ON mv.ahorrador_id = d.ahorrador_id
             WHERE d.periodo_id = @periodo AND ABS(d.total - COALESCE(mv.m, 0)) > 0.005
            """, new { periodo = e.Periodos[0].Id });
        Assert.Equal(0, diferencias);
        Assert.Equal(b.Id, movs.Single(m => m.AhorradorId == b.Id).AhorradorId);
    }

    [Fact]
    public async Task Aplicar_dos_veces_el_mismo_periodo_falla_y_no_duplica_movimientos()
    {
        var e = await Entorno.CrearAsync(fx);
        await e.AhorradorAsync(1_000m);
        await e.AhorradorAsync(800m);
        await AplicarAsync(e, 0);

        var error = await Error(() => AplicarAsync(e, 0));
        Assert.Equal(Codigos.PeriodoYaAplicado, error.Codigo);
        Assert.Equal(409, error.Estado);
        Assert.Equal(2, (await e.MovimientosAsync(0)).Count);
    }

    [Fact]
    public async Task Cinco_peticiones_simultaneas_aplican_el_periodo_una_sola_vez()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 8_000m);
        for (var i = 0; i < 5; i++) await e.AhorradorAsync(500m + i);
        await e.PrestamoVigenteAsync(a, 6_000m, 4);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            try { return (Ok: await AplicarAsync(e, 0), Error: (ErrorNegocio?)null); }
            catch (ErrorNegocio ex) { return (Ok: null, Error: ex); }
        }));

        Assert.Single(resultados, r => r.Ok is not null);
        Assert.All(resultados.Where(r => r.Error is not null), r => Assert.Equal(Codigos.PeriodoYaAplicado, r.Error!.Codigo));
        // 6 ahorradores con ordinaria + 2 abonos del préstamo: ni uno más.
        Assert.Equal(8, (await e.MovimientosAsync(0)).Count);
    }

    [Fact]
    public async Task Los_periodos_se_aplican_en_orden()
    {
        var e = await Entorno.CrearAsync(fx);
        await e.AhorradorAsync(1_000m);

        var error = await Error(() => AplicarAsync(e, 1));
        Assert.Equal(Codigos.PeriodoNoSiguiente, error.Codigo);
        Assert.Empty(await e.MovimientosAsync(1));

        await AplicarAsync(e, 0);
        await AplicarAsync(e, 1);   // ahora sí
    }

    [Fact]
    public async Task Un_periodo_que_falla_a_la_mitad_se_revierte_completo()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 10_000m);
        await e.AhorradorAsync(800m);
        var prestamo = await e.PrestamoVigenteAsync(a, 6_000m, 4);

        // Una cuota con capital cero produce un movimiento con monto cero, que la tabla rechaza justo después de que
        // el periodo ya cambió de estado y se insertaron los movimientos de ahorro.
        await using (var db = e.Db())
            await db.EjecutarAsync(
                "UPDATE prestamo_amortizacion SET capital = 0, interes = 5, total = 5 WHERE prestamo_id = @id AND numero = 1",
                new { id = prestamo.Id });

        await Assert.ThrowsAsync<DbUpdateException>(() => AplicarAsync(e, 0));

        await using var db2 = e.Db();
        Assert.Equal(EstadoPeriodo.Abierto, (await db2.Periodos.FindAsync(e.Periodos[0].Id))!.Estado);
        Assert.Empty(await e.MovimientosAsync(0));
        Assert.Equal(EstadoAmortizacion.Programado,
            (await db2.Amortizaciones.SingleAsync(x => x.PrestamoId == prestamo.Id && x.Numero == 1)).Estado);
    }

    [Fact]
    public async Task Una_licencia_recorre_la_cuota_del_prestamo_un_periodo_sin_cobrarla()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 10_000m);
        var prestamo = await e.PrestamoVigenteAsync(a, 12_000m, 6);
        await e.IncidenciaAsync(a, 0, TipoIncidencia.Licencia);

        var r = await AplicarAsync(e, 0);
        Assert.Equal(1, r.CuotasDiferidas);
        Assert.Equal(0, r.MovimientosGenerados);

        await using (var db = e.Db())
        {
            Assert.Equal(EstadoAhorrador.Licencia, (await db.Ahorradores.FindAsync(a.Id))!.Estado);
            var cuotas = await db.Amortizaciones.AsNoTracking().Where(x => x.PrestamoId == prestamo.Id).OrderBy(x => x.Numero).ToListAsync();
            Assert.All(cuotas, c => Assert.Equal(EstadoAmortizacion.Programado, c.Estado));
            // La cuota 1 pasó al periodo 2 y la última, del 6 al 7; los importes no cambian.
            Assert.Equal(e.Periodos[1].Id, cuotas[0].PeriodoId);
            Assert.Equal(e.Periodos[6].Id, cuotas[5].PeriodoId);
            Assert.Equal(e.Periodos[6].FechaFin, cuotas[5].Fecha);
            Assert.Equal(2_090m, cuotas[0].Total);
        }

        // Al regresar, el periodo siguiente cobra la cuota 1 y el ahorrador vuelve a estar activo.
        var siguiente = await AplicarAsync(e, 1);
        Assert.Equal(2_000m, siguiente.CapitalPrestamos);
        await using var db2 = e.Db();
        Assert.Equal(EstadoAhorrador.Activo, (await db2.Ahorradores.FindAsync(a.Id))!.Estado);
    }

    [Fact]
    public async Task La_ultima_cuota_aplicada_liquida_el_prestamo()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(500m, ahorro: 10_000m);
        var prestamo = await e.PrestamoVigenteAsync(a, 3_000m, 2);

        Assert.Equal(0, (await AplicarAsync(e, 0)).PrestamosLiquidados);
        Assert.Equal(1, (await AplicarAsync(e, 1)).PrestamosLiquidados);

        await using var db = e.Db();
        Assert.Equal(EstadoPrestamo.Liquidado, (await db.Prestamos.FindAsync(prestamo.Id))!.Estado);
        var saldo = await db.EscalarAsync<decimal>(
            "SELECT saldo_vigente FROM v_prestamo_resumen WHERE prestamo_id = @id", new { id = prestamo.Id });
        Assert.Equal(0m, saldo);
    }

    [Fact]
    public async Task Una_baja_liquida_el_ahorro_y_deja_de_descontar()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 5_000m);
        var otro = await e.AhorradorAsync(700m);

        await using (var db = e.Db())
            await e.Nomina(db).CrearIncidenciaAsync(new CrearIncidenciaRequest(
                a.Id, e.Periodos[0].Id, TipoIncidencia.Baja, null, "Renuncia", e.Periodos[0].FechaInicio.AddDays(3)));

        await using (var db = e.Db())
        {
            var guardado = await db.Ahorradores.FindAsync(a.Id);
            Assert.Equal(EstadoAhorrador.Baja, guardado!.Estado);
            Assert.Equal(e.Periodos[0].FechaInicio.AddDays(3), guardado.FechaBaja);
            var liquidacion = await db.Movimientos.SingleAsync(m => m.AhorradorId == a.Id && m.Tipo == TipoMovimiento.LiquidacionCiclo);
            Assert.Equal(5_000m, liquidacion.Monto);
            Assert.Equal("BAJA", liquidacion.Referencia);
            Assert.Equal(0m, await db.EscalarAsync<decimal>("SELECT total FROM v_saldo_ahorrador WHERE ahorrador_id = @id", new { id = a.Id }));
        }

        var r = await AplicarAsync(e, 0);
        Assert.Equal(1, r.MovimientosGenerados);       // solo el otro ahorrador
        Assert.Equal(otro.Id, (await e.MovimientosAsync(0)).Single().AhorradorId);
    }

    [Fact]
    public async Task No_se_da_de_baja_a_quien_debe_un_prestamo_ni_a_quien_avala_uno()
    {
        var e = await Entorno.CrearAsync(fx);
        var deudor = await e.AhorradorAsync(1_000m, ahorro: 10_000m);
        var aval = await e.AhorradorAsync(1_000m, ahorro: 10_000m);
        await e.PrestamoVigenteAsync(deudor, 12_000m, 6, avalId: aval.Id, excedenteAval: 3_000m);

        CrearIncidenciaRequest Baja(int id) =>
            new(id, e.Periodos[0].Id, TipoIncidencia.Baja, null, null, e.Periodos[0].FechaInicio);

        await using var db = e.Db();
        Assert.Equal(Codigos.BajaConPrestamo, (await Error(() => e.Nomina(db).CrearIncidenciaAsync(Baja(deudor.Id)))).Codigo);
        await using var db2 = e.Db();
        Assert.Equal(Codigos.BajaConAval, (await Error(() => e.Nomina(db2).CrearIncidenciaAsync(Baja(aval.Id)))).Codigo);
        await using var db3 = e.Db();
        Assert.All(await db3.Ahorradores.Where(x => x.CajaId == e.CajaId).ToListAsync(), x => Assert.Equal(EstadoAhorrador.Activo, x.Estado));
    }

    [Fact]
    public async Task Las_incidencias_exigen_periodo_abierto_y_no_se_repiten()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m);
        await using (var db = e.Db())
            await e.Nomina(db).CrearIncidenciaAsync(new CrearIncidenciaRequest(a.Id, e.Periodos[0].Id, TipoIncidencia.Licencia, null, null, null));

        await using var db2 = e.Db();
        Assert.Equal(Codigos.IncidenciaDuplicada, (await Error(() => e.Nomina(db2).CrearIncidenciaAsync(
            new CrearIncidenciaRequest(a.Id, e.Periodos[0].Id, TipoIncidencia.Licencia, null, null, null)))).Codigo);

        await AplicarAsync(e, 0);
        await using var db3 = e.Db();
        Assert.Equal(Codigos.PeriodoNoAbierto, (await Error(() => e.Nomina(db3).CrearIncidenciaAsync(
            new CrearIncidenciaRequest(a.Id, e.Periodos[0].Id, TipoIncidencia.Incapacidad, null, null, null)))).Codigo);
    }

    [Fact]
    public async Task El_cambio_de_cuota_rige_desde_el_inicio_del_periodo_y_conserva_el_voluntario()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, voluntaria: 250m);
        await using (var db = e.Db())
            await e.Nomina(db).CrearIncidenciaAsync(new CrearIncidenciaRequest(a.Id, e.Periodos[0].Id, TipoIncidencia.CambioCuota, 1_250m, null, null));

        var r = await AplicarAsync(e, 0);
        Assert.Equal(1_250m, r.AhorroOrdinario);
        Assert.Equal(250m, r.AhorroVoluntario);
    }

    [Fact]
    public async Task El_layout_excluye_a_los_suspendidos_y_registra_el_historial()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 10_000m, voluntaria: 200m);
        var suspendido = await e.AhorradorAsync(900m);
        await e.PrestamoVigenteAsync(a, 12_000m, 6);
        await e.IncidenciaAsync(suspendido, 0, TipoIncidencia.Incapacidad);

        await using var db = e.Db();
        var servicio = e.Nomina(db);
        var archivo = await servicio.GenerarLayoutAsync(e.Periodos[0].Id, FormatoLayout.Csv);

        Assert.Equal(3, archivo.Registros);            // ordinario, voluntario y préstamo de un solo ahorrador
        Assert.Equal(1_000m + 200m + 2_090m, archivo.ImporteTotal);
        var texto = System.Text.Encoding.UTF8.GetString(archivo.Contenido);
        Assert.DoesNotContain(suspendido.NumEmpleado, texto);
        Assert.Contains("TOTAL,3,3290.00", texto);
        Assert.Equal($"nomina_TST_Q1_2024.csv", archivo.NombreArchivo);

        var historial = await servicio.ListarLayoutsAsync(new ConsultaLayouts { PeriodoId = e.Periodos[0].Id });
        var fila = Assert.Single(historial.Items);
        Assert.Equal(archivo.SumaControl, fila.SumaControl);
        Assert.Equal("CSV", fila.Formato);
        Assert.Equal("Q1-2024", fila.Periodo);
    }

    [Fact]
    public async Task Sin_descuentos_no_hay_layout()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m);
        await e.IncidenciaAsync(a, 0, TipoIncidencia.Licencia);

        await using var db = e.Db();
        var error = await Error(() => e.Nomina(db).GenerarLayoutAsync(e.Periodos[0].Id, FormatoLayout.TxtPipe));
        Assert.Equal(Codigos.LayoutSinRegistros, error.Codigo);
    }

    [Fact]
    public async Task Una_caja_semanal_aplica_sus_periodos_con_referencia_semanal()
    {
        var e = await Entorno.CrearAsync(fx, FrecuenciaNomina.Semanal);
        await e.AhorradorAsync(300m);

        var r = await AplicarAsync(e, 0);
        Assert.Equal("S1-2024", r.Referencia);
        Assert.Equal("S1-2024", (await e.MovimientosAsync(0)).Single().Referencia);
    }
}

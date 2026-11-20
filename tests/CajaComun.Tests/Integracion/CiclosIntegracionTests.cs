using System.Text;
using CajaComun.Application.Ciclos;
using CajaComun.Application.Comun;
using CajaComun.Application.Nomina;
using CajaComun.Application.Reportes;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;
using CajaComun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CajaComun.Tests.Integracion;

/// <summary>Registra cada invalidación para comprobar qué operaciones refrescan el caché.</summary>
public sealed class CacheEspia : ICacheDatos
{
    public List<int> Invalidaciones { get; } = [];
    public Task<T> ObtenerOCrearAsync<T>(string clave, int? cajaId, TimeSpan vigencia, Func<Task<T>> crear) => crear();
    public Task InvalidarAsync(int cajaId) { lock (Invalidaciones) Invalidaciones.Add(cajaId); return Task.CompletedTask; }
}

[Collection(ColeccionMariaDb.Nombre)]
[Trait("Category", Categorias.Integracion)]
public class CiclosIntegracionTests(MariaDbFixture fx)
{
    private static async Task<ErrorNegocio> Error(Func<Task> accion) => await Assert.ThrowsAsync<ErrorNegocio>(accion);

    private sealed record Escenario(Entorno E, Ahorrador A, Ahorrador B, Ahorrador C, Ahorrador D, Ahorrador Baja);

    /// <summary>
    /// Ciclo 2024 completo. A y B pagan préstamos que terminan en el año; C tiene uno que sigue vigente al cierre; D no
    /// debe nada; la baja sale a mediados de año. Intereses de préstamo cobrados: 112.50 (A) + 315.00 (B) + los de C.
    /// </summary>
    private async Task<Escenario> ArmarAsync(bool aplicarTodo = true)
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m);
        var b = await e.AhorradorAsync(500m);
        var c = await e.AhorradorAsync(700m);
        var d = await e.AhorradorAsync(600m);
        var baja = await e.AhorradorAsync(400m, ahorro: 0m);
        await e.PrestamoVigenteAsync(a, 6_000m, 4);                        // Q1-Q4
        await e.PrestamoVigenteAsync(b, 12_000m, 6);                       // Q1-Q6
        await e.PrestamoVigenteAsync(c, 20_000m, 20, primerPeriodo: 19);   // Q20 en adelante: sigue vigente al cierre
        if (!aplicarTodo) return new Escenario(e, a, b, c, d, baja);

        await e.AplicarAnio2024Async(0, 8);
        await using (var db = e.Db())
            await e.Nomina(db).CrearIncidenciaAsync(new CrearIncidenciaRequest(
                baja.Id, e.Periodos[9].Id, TipoIncidencia.Baja, null, null, e.Periodos[9].FechaInicio.AddDays(2)));
        await e.AplicarAnio2024Async(9, 23);
        return new Escenario(e, a, b, c, d, baja);
    }

    private static BorradorCicloRequest Borrador(Entorno e) => new(e.CajaId, 2024);

    private async Task<(CicloDto Ciclo, List<DistribucionDto> Filas)> BorradorAsync(Entorno e)
    {
        await using var db = e.Db();
        var servicio = e.Ciclos(db);
        var resumen = await servicio.BorradorAsync(Borrador(e));
        return (resumen.Ciclo, [.. await servicio.DistribucionCompletaAsync(resumen.Ciclo.Id)]);
    }

    [Fact]
    public async Task El_borrador_reparte_el_interes_cobrado_exactamente_y_el_saldo_dias_coincide_con_el_dominio()
    {
        var s = await ArmarAsync();
        var (ciclo, filas) = await BorradorAsync(s.E);

        await using var db = s.E.Db();
        var cobrado = await db.Movimientos.Where(m => m.CajaId == s.E.CajaId && m.Tipo == TipoMovimiento.AbonoInteres).SumAsync(m => m.Monto);
        Assert.True(cobrado > 427.5m);                                        // 427.50 de A y B más lo de C
        Assert.Equal(EstadoCiclo.EnCierre, ciclo.Estado);
        Assert.Equal(cobrado, ciclo.InteresTotal);
        Assert.Equal(cobrado, filas.Sum(f => f.InteresAsignado));              // centavo por centavo

        // El procedimiento almacenado debe dar lo mismo que la regla de dominio calculada desde los movimientos.
        var inicio = new DateOnly(2024, 1, 1);
        var fin = new DateOnly(2024, 12, 31);
        foreach (var f in filas)
        {
            var movs = await db.Movimientos.Where(m => m.AhorradorId == f.AhorradorId && m.Fecha >= inicio && m.Fecha <= fin).ToListAsync();
            var aportes = movs.Where(m => m.Tipo is TipoMovimiento.AhorroOrdinario or TipoMovimiento.AhorroVoluntario)
                .Select(m => new CambioAhorro(m.Fecha, m.Monto));
            var liquidaciones = movs.Where(m => m.Tipo == TipoMovimiento.LiquidacionCiclo).Select(m => new CambioAhorro(m.Fecha, m.Monto));
            var esperado = SaldoDias.Calcular(aportes, liquidaciones, inicio, fin);
            Assert.Equal(esperado.SaldoDias, f.SaldoDias);
            Assert.Equal(esperado.DiasActivos, f.DiasActivos);
        }

        var total = filas.Sum(f => f.SaldoDias);
        Assert.All(filas, f => Assert.Equal(decimal.Round(f.SaldoDias / total, 6), f.Participacion));
    }

    [Fact]
    public async Task La_baja_a_mitad_de_ano_participa_solo_por_sus_dias_y_no_se_le_liquida_dos_veces()
    {
        var s = await ArmarAsync();
        var (_, filas) = await BorradorAsync(s.E);

        var baja = filas.Single(f => f.AhorradorId == s.Baja.Id);
        var completo = filas.Single(f => f.AhorradorId == s.D.Id);
        Assert.True(baja.DiasActivos < 200 && baja.DiasActivos > 100, baja.DiasActivos.ToString());
        // Cuenta desde su primera aportación (fin de la Q1, 15-ene) hasta el 31-dic.
        Assert.Equal(new DateOnly(2024, 12, 31).DayNumber - new DateOnly(2024, 1, 15).DayNumber + 1, completo.DiasActivos);
        Assert.True(baja.SaldoDias > 0m);
        Assert.Equal(0m, baja.AhorroLiquidado);                                  // ya se liquidó al darse de baja
        Assert.Equal(0m, baja.AhorroRetenido);
        Assert.True(baja.InteresAsignado > 0m);
    }

    [Fact]
    public async Task Quien_tiene_prestamo_vigente_retiene_ahorro_como_garantia_y_los_demas_liquidan_todo()
    {
        var s = await ArmarAsync();
        var (_, filas) = await BorradorAsync(s.E);

        var d = filas.Single(f => f.AhorradorId == s.D.Id);
        Assert.Equal(0m, d.AhorroRetenido);
        Assert.Equal(600m * 24, d.AhorroLiquidado);

        // C debe 15,000 (le faltan 15 de 20 cuotas de 1,000): se retiene lo que el préstamo necesita, 18,750, sin pasar de su ahorro.
        var c = filas.Single(f => f.AhorradorId == s.C.Id);
        Assert.Equal(700m * 24, c.AhorroLiquidado + c.AhorroRetenido);
        Assert.Equal(ReglasCiclo.AhorroRetenido(700m * 24, 20_000m - 5_000m), c.AhorroRetenido);
    }

    [Fact]
    public async Task El_borrador_se_puede_recalcular_mientras_el_ciclo_no_se_cierre()
    {
        var s = await ArmarAsync();
        var (ciclo, primero) = await BorradorAsync(s.E);

        await using (var db = s.E.Db())
        {
            db.Movimientos.Add(new Movimiento
            {
                CajaId = s.E.CajaId, AhorradorId = s.D.Id, Fecha = new DateOnly(2024, 12, 20), Tipo = TipoMovimiento.AhorroVoluntario, Monto = 5_000m
            });
            await db.SaveChangesAsync();
        }
        var (mismo, segundo) = await BorradorAsync(s.E);

        Assert.Equal(ciclo.Id, mismo.Id);                                        // no crea otro ciclo
        Assert.Equal(primero.Count, segundo.Count);
        Assert.True(segundo.Single(f => f.AhorradorId == s.D.Id).SaldoDias > primero.Single(f => f.AhorradorId == s.D.Id).SaldoDias);
        Assert.Equal(mismo.InteresTotal, segundo.Sum(f => f.InteresAsignado));
    }

    [Fact]
    public async Task No_se_cierra_con_periodos_sin_aplicar_y_el_resumen_lo_explica()
    {
        var s = await ArmarAsync(aplicarTodo: false);
        await s.E.AplicarAnio2024Async(0, 2);

        await using var db = s.E.Db();
        var servicio = s.E.Ciclos(db);
        var resumen = await servicio.BorradorAsync(Borrador(s.E));
        Assert.False(resumen.PuedeCerrar);
        Assert.Equal((24, 3), (resumen.PeriodosTotales, resumen.PeriodosAplicados));
        Assert.Equal([$"{Codigos.CicloPeriodosAbiertos}:21"], resumen.Impedimentos);

        var error = await Error(() => servicio.CerrarAsync(resumen.Ciclo.Id));
        Assert.Equal(Codigos.CicloPeriodosAbiertos, error.Codigo);
        await using var db2 = s.E.Db();
        Assert.Equal(EstadoCiclo.EnCierre, (await db2.Ciclos.SingleAsync(c => c.CajaId == s.E.CajaId && c.Anio == 2024)).Estado);
        Assert.Equal(0, await db2.Movimientos.CountAsync(m => m.CajaId == s.E.CajaId && m.Tipo == TipoMovimiento.InteresAnual));
    }

    [Fact]
    public async Task Cerrar_genera_los_movimientos_cierra_los_periodos_y_abre_el_ciclo_siguiente()
    {
        var s = await ArmarAsync();
        var (ciclo, filas) = await BorradorAsync(s.E);
        var interes = ciclo.InteresTotal;

        CierreCicloResultadoDto cierre;
        await using (var db = s.E.Db()) cierre = await s.E.Ciclos(db).CerrarAsync(ciclo.Id);

        await using var v = s.E.Db();
        Assert.Equal(interes, cierre.InteresTotal);
        var guardado = await v.Ciclos.AsNoTracking().SingleAsync(c => c.Id == ciclo.Id);
        Assert.Equal(EstadoCiclo.Cerrado, guardado.Estado);
        Assert.NotNull(guardado.FechaCierre);
        Assert.Equal(EstadoCiclo.Abierto, (await v.Ciclos.SingleAsync(c => c.CajaId == s.E.CajaId && c.Anio == 2025)).Estado);
        Assert.All(await v.Periodos.Where(p => p.CajaId == s.E.CajaId && p.Anio == 2024).ToListAsync(), p => Assert.Equal(EstadoPeriodo.Cerrado, p.Estado));
        Assert.All(await v.Periodos.Where(p => p.CajaId == s.E.CajaId && p.Anio == 2025).ToListAsync(), p => Assert.Equal(EstadoPeriodo.Abierto, p.Estado));

        var movs = await v.Movimientos.AsNoTracking().Where(m => m.CajaId == s.E.CajaId && m.Referencia != null &&
            (m.Referencia == "CIERRE-2024" || m.Referencia == "SALDO-INICIAL-2025")).ToListAsync();
        Assert.Equal(interes, movs.Where(m => m.Tipo == TipoMovimiento.InteresAnual).Sum(m => m.Monto));
        Assert.Equal(cierre.MovimientosGenerados, movs.Count);
        Assert.All(movs.Where(m => m.Referencia == "CIERRE-2024"), m => Assert.Equal(new DateOnly(2024, 12, 31), m.Fecha));
        Assert.All(movs.Where(m => m.Referencia == "SALDO-INICIAL-2025"), m => Assert.Equal(new DateOnly(2025, 1, 1), m.Fecha));

        // Tras el cierre el ahorro es cero para quien no debe nada y lo retenido para quien sí.
        decimal Saldo(int id) => v.EscalarAsync<decimal>("SELECT total FROM v_saldo_ahorrador WHERE ahorrador_id = @id", new { id }).Result;
        Assert.Equal(0m, Saldo(s.D.Id));
        Assert.Equal(0m, Saldo(s.A.Id));
        var c = filas.Single(f => f.AhorradorId == s.C.Id);
        Assert.Equal(c.AhorroRetenido, Saldo(s.C.Id));
        Assert.Equal(cierre.AhorroLiquidado, filas.Sum(f => f.AhorroLiquidado));
    }

    [Fact]
    public async Task Un_ciclo_cerrado_es_inmutable_y_no_se_cierra_dos_veces()
    {
        var s = await ArmarAsync();
        var (ciclo, _) = await BorradorAsync(s.E);
        await using (var db = s.E.Db()) await s.E.Ciclos(db).CerrarAsync(ciclo.Id);

        await using var db2 = s.E.Db();
        Assert.Equal(Codigos.CicloYaCerrado, (await Error(() => s.E.Ciclos(db2).CerrarAsync(ciclo.Id))).Codigo);
        await using var db3 = s.E.Db();
        Assert.Equal(Codigos.CicloYaCerrado, (await Error(() => s.E.Ciclos(db3).BorradorAsync(Borrador(s.E)))).Codigo);
    }

    [Fact]
    public async Task Cuatro_cierres_simultaneos_cierran_una_sola_vez_sin_duplicar_movimientos()
    {
        var s = await ArmarAsync();
        var (ciclo, filas) = await BorradorAsync(s.E);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            try
            {
                await using var db = s.E.Db();
                return (Ok: await s.E.Ciclos(db).CerrarAsync(ciclo.Id), Error: (ErrorNegocio?)null);
            }
            catch (ErrorNegocio ex) { return (Ok: null, Error: ex); }
        }));

        Assert.Single(resultados, r => r.Ok is not null);
        Assert.All(resultados.Where(r => r.Error is not null), r => Assert.Equal(Codigos.CicloYaCerrado, r.Error!.Codigo));
        await using var v = s.E.Db();
        Assert.Equal(filas.Count(f => f.InteresAsignado > 0m),
            await v.Movimientos.CountAsync(m => m.CajaId == s.E.CajaId && m.Tipo == TipoMovimiento.InteresAnual));
    }

    [Fact]
    public async Task El_spei_dispersa_lo_liquidado_mas_el_rendimiento_y_lo_retenido_no_sale()
    {
        var s = await ArmarAsync();
        var (ciclo, filas) = await BorradorAsync(s.E);
        await using (var db = s.E.Db()) await s.E.Ciclos(db).CerrarAsync(ciclo.Id);

        await using var v = s.E.Db();
        var servicio = s.E.Ciclos(v);
        var completo = await servicio.GenerarSpeiAsync(ciclo.Id, FormatoSpei.GenericoCsv, incluirAhorro: true);
        var esperado = filas.Sum(f => f.AhorroLiquidado + f.InteresAsignado);
        Assert.Equal(esperado, completo.ImporteTotal);
        Assert.Equal(filas.Count(f => f.AhorroLiquidado + f.InteresAsignado > 0m), completo.Registros);
        Assert.Equal($"SPEI-TST-2024.csv", completo.NombreArchivo);

        var lineas = Encoding.UTF8.GetString(completo.Contenido).Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Skip(1)
            .Select(l => l.Split(',')).ToList();
        Assert.Equal(esperado, lineas.Sum(l => decimal.Parse(l[2], System.Globalization.CultureInfo.InvariantCulture)));
        // Siguiente día hábil a hoy (el cierre fue hoy, en el reloj real de la prueba).
        var hoy = DateOnly.FromDateTime(DateTime.UtcNow);
        Assert.All(lineas, l => Assert.Equal(ReglasCiclo.SiguienteDiaHabil(hoy).ToString("yyyy-MM-dd"), l[5]));

        var soloRendimiento = await servicio.GenerarSpeiAsync(ciclo.Id, FormatoSpei.GenericoCsv, incluirAhorro: false);
        Assert.Equal(filas.Sum(f => f.InteresAsignado), soloRendimiento.ImporteTotal);

        var banorte = await servicio.GenerarSpeiAsync(ciclo.Id, FormatoSpei.BanorteSimplificado, incluirAhorro: true);
        Assert.Equal(completo.SumaControl, banorte.SumaControl);
        Assert.EndsWith("-BANORTE.txt", banorte.NombreArchivo);

        var historial = await servicio.ListarLayoutsAsync(ciclo.Id, new ConsultaPaginada());
        Assert.Equal(3, historial.Total);
        Assert.Equal(["BANORTE_SIMPLIFICADO", "GENERICO_CSV", "GENERICO_CSV"], historial.Items.Select(l => l.Formato).Order());
        Assert.All(historial.Items, l => Assert.Equal(TipoLayout.Spei, l.Tipo));
    }

    [Fact]
    public async Task El_spei_exige_un_ciclo_cerrado_y_rechaza_clabes_invalidas()
    {
        var s = await ArmarAsync();
        var (ciclo, _) = await BorradorAsync(s.E);

        await using (var db = s.E.Db())
            Assert.Equal(Codigos.CicloNoCerrado, (await Error(() => s.E.Ciclos(db).GenerarSpeiAsync(ciclo.Id, FormatoSpei.GenericoCsv, true))).Codigo);

        await using (var db = s.E.Db()) await s.E.Ciclos(db).CerrarAsync(ciclo.Id);
        await using (var db = s.E.Db())
            await db.EjecutarAsync("UPDATE ahorrador SET clabe = '012180001234567890' WHERE id = @id", new { id = s.D.Id });

        await using var v = s.E.Db();
        var error = await Error(() => s.E.Ciclos(v).GenerarSpeiAsync(ciclo.Id, FormatoSpei.GenericoCsv, true));
        Assert.Equal(Codigos.SpeiRenglonesInvalidos, error.Codigo);
        Assert.Equal([$"{s.D.NumEmpleado}:CLABE_INVALIDA"], error.Detalles!["renglones"]);
        await using var v2 = s.E.Db();
        Assert.Equal(0, await v2.Layouts.CountAsync(l => l.CicloId == ciclo.Id));        // no queda un layout a medias
    }

    [Fact]
    public async Task Un_ciclo_de_otra_caja_responde_404_y_el_borrador_exige_acceso_a_la_caja()
    {
        var s = await ArmarAsync(aplicarTodo: false);
        await using var db = s.E.Db();
        var servicio = s.E.Ciclos(db);
        var ciclo = await servicio.BorradorAsync(Borrador(s.E));

        // Un usuario sin cajas asignadas.
        int sinCajas;
        await using (var d = s.E.Db())
        {
            var u = new Usuario { Nombre = "Sin cajas", Correo = $"x-{Guid.NewGuid():N}@prueba.mx", HashPassword = "-", Rol = RolUsuario.Administrador };
            d.Usuarios.Add(u);
            await d.SaveChangesAsync();
            sinCajas = u.Id;
        }
        await using var db2 = s.E.Db();
        var ajeno = s.E.Ciclos(db2, sinCajas);
        Assert.Equal(404, (await Error(() => ajeno.ResumenAsync(ciclo.Ciclo.Id))).Estado);
        await using var db3 = s.E.Db();
        Assert.Equal(403, (await Error(() => s.E.Ciclos(db3, sinCajas).BorradorAsync(Borrador(s.E)))).Estado);
        await using var db4 = s.E.Db();
        Assert.Empty(await s.E.Ciclos(db4, sinCajas).ListarAsync(null));
    }

    // ---------- Reportes y panel ----------

    [Fact]
    public async Task El_estado_de_resultados_reparte_los_intereses_por_mes_y_cuadra_con_el_ciclo()
    {
        var s = await ArmarAsync();
        var (ciclo, _) = await BorradorAsync(s.E);
        await using (var db = s.E.Db()) await s.E.Ciclos(db).CerrarAsync(ciclo.Id);

        await using var v = s.E.Db();
        var r = await s.E.Reportes(v).EstadoResultadosAsync(s.E.CajaId, 2024);
        Assert.Equal(12, r.Meses.Count);
        Assert.Equal(ciclo.InteresTotal, r.InteresesCobrados);
        Assert.Equal(r.InteresesCobrados, r.Meses.Sum(m => m.Cobrados));
        Assert.Equal(r.InteresesCobrados, r.UtilidadADistribuir);
        Assert.Equal(r.InteresesCobrados, r.InteresDistribuido);
        Assert.Equal(EstadoCiclo.Cerrado, r.EstadoCiclo);
        Assert.Equal(1, r.PrestamosVigentes);                                    // el de C
        Assert.Equal(15_000m, r.SaldoCartera);
        Assert.True(r.Meses[0].Cobrados > r.Meses[2].Cobrados);                  // los intereses bajan al amortizar
    }

    [Fact]
    public async Task Los_reportes_se_generan_en_pdf_excel_y_json()
    {
        var s = await ArmarAsync();
        var (ciclo, _) = await BorradorAsync(s.E);
        await using (var db = s.E.Db()) await s.E.Ciclos(db).CerrarAsync(ciclo.Id);

        await using var v = s.E.Db();
        var reportes = s.E.Reportes(v);
        foreach (var formato in new[] { FormatoReporte.Pdf, FormatoReporte.Xlsx, FormatoReporte.Json })
        {
            var distribucion = await reportes.DistribucionArchivoAsync(ciclo.Id, formato);
            var estado = await reportes.EstadoResultadosArchivoAsync(s.E.CajaId, 2024, formato);
            var cuenta = await reportes.EstadoCuentaArchivoAsync(s.A.Id, 2024, formato);
            foreach (var a in new[] { distribucion, estado, cuenta })
            {
                Assert.True(a.Contenido.Length > 200, a.NombreArchivo);
                Assert.EndsWith(formato switch { FormatoReporte.Pdf => ".pdf", FormatoReporte.Xlsx => ".xlsx", _ => ".json" }, a.NombreArchivo);
            }
            Assert.Equal(formato == FormatoReporte.Pdf, Encoding.ASCII.GetString(distribucion.Contenido, 0, 5) == "%PDF-");
        }
    }

    [Fact]
    public async Task El_dashboard_suma_el_fondo_y_la_cartera_de_las_cajas_y_arma_la_serie_de_12_meses()
    {
        var s = await ArmarAsync();
        await BorradorAsync(s.E);                                               // abre el ciclo 2024 de la caja
        await using var v = s.E.Db();
        var d = await s.E.Dashboard(v).ObtenerAsync(s.E.CajaId);

        var fondo = await v.EscalarAsync<decimal>(
            "SELECT CAST(SUM(s.total) AS DECIMAL(14,2)) FROM ahorrador a JOIN v_saldo_ahorrador s ON s.ahorrador_id = a.id WHERE a.caja_id = @c AND a.estado <> 'BAJA'",
            new { c = s.E.CajaId });
        Assert.Equal(fondo, d.FondoTotal);
        Assert.Equal(4, d.AhorradoresActivos);                                   // la baja no cuenta
        Assert.Equal(1, d.PrestamosVigentes);
        Assert.Equal(15_000m, d.SaldoPrestamos);
        Assert.Equal(2024, d.AnioCiclo);
        Assert.Equal(12, d.Serie.Count);
        Assert.Equal((2024, 1), (d.Serie[0].Anio, d.Serie[0].Mes));
        Assert.Equal((2024, 12), (d.Serie[^1].Anio, d.Serie[^1].Mes));
        Assert.True(d.Serie.All(p => p.AhorroAportado > 0m));
        Assert.Equal(8, d.UltimosMovimientos.Count);
        Assert.True(d.InteresAcumuladoCiclo > 427.5m);
    }

    [Fact]
    public async Task Las_operaciones_que_cambian_datos_invalidan_el_cache_de_su_caja()
    {
        var e = await Entorno.CrearAsync(fx);
        var a = await e.AhorradorAsync(1_000m, ahorro: 20_000m);
        var espia = new CacheEspia();

        await using (var db = e.Db()) await e.Nomina(db, cache: espia).AplicarPeriodoAsync(e.Periodos[0].Id);
        await using (var db = e.Db())
        {
            var p = e.Prestamos(db, cache: espia);
            var creado = await p.CrearAsync(new CajaComun.Application.Prestamos.SolicitudPrestamoRequest(a.Id, 5_000m, 4, null));
            await p.AutorizarAsync(creado.Prestamo.Id);
        }

        Assert.True(espia.Invalidaciones.Count >= 3, string.Join(",", espia.Invalidaciones));
        Assert.All(espia.Invalidaciones, id => Assert.Equal(e.CajaId, id));
    }
}

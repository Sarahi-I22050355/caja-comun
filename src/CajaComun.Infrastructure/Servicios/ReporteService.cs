using System.Globalization;
using System.Text.Json;
using CajaComun.Application.Ahorradores;
using CajaComun.Application.Ciclos;
using CajaComun.Application.Comun;
using CajaComun.Application.Nomina;
using CajaComun.Application.Reportes;
using CajaComun.Domain.Enums;
using CajaComun.Infrastructure.Archivos;
using CajaComun.Infrastructure.Persistence;

namespace CajaComun.Infrastructure.Servicios;

public sealed class ReporteService(
    CajaComunDbContext db, ICajaAcceso acceso, IAhorradorService ahorradores, ICicloService ciclos, ICacheDatos cache)
    : IReporteService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string Dinero(decimal d) => "$" + d.ToString("#,##0.00", Inv);

    // ---------- Estado de resultados ----------

    private sealed class FilaMes
    {
        public int Mes { get; set; }
        public decimal Monto { get; set; }
    }

    private sealed class FilaCartera
    {
        public decimal Saldo { get; set; }
        public long Cuenta { get; set; }
    }

    private sealed class FilaCicloAnio
    {
        public string Estado { get; set; } = "";
        public decimal Distribuido { get; set; }
    }

    public async Task<EstadoResultadosDto> EstadoResultadosAsync(int cajaId, int anio, CancellationToken ct = default)
    {
        await acceso.ExigirAsync(cajaId, ct);
        var caja = await db.Cajas.FindAsync([cajaId], ct)
            ?? throw ErrorNegocio.NoEncontrado(Codigos.CajaNoEncontrada, "La caja no existe.");
        var nombre = caja.Nombre;

        // Las entradas dependen de la versión de datos de la caja: cualquier aplicación de periodo, préstamo o cierre las invalida.
        return await cache.ObtenerOCrearAsync($"estado-resultados:{anio}", cajaId, TimeSpan.FromMinutes(10), async () =>
        {
            var inicio = new DateTime(anio, 1, 1);
            var fin = new DateTime(anio, 12, 31);
            var cobrados = await db.ConsultarAsync<FilaMes>(
                """
                SELECT MONTH(fecha) AS Mes, CAST(SUM(monto) AS DECIMAL(14,2)) AS Monto FROM movimiento
                 WHERE caja_id = @caja AND tipo = 'ABONO_INTERES' AND fecha BETWEEN @inicio AND @fin GROUP BY MONTH(fecha)
                """, new { caja = cajaId, inicio, fin }, ct);
            var devengados = await db.ConsultarAsync<FilaMes>(
                """
                SELECT MONTH(d.fecha_corte) AS Mes, CAST(SUM(d.interes) AS DECIMAL(14,2)) AS Monto
                  FROM interes_devengado d JOIN prestamo p ON p.id = d.prestamo_id
                 WHERE p.caja_id = @caja AND d.fecha_corte BETWEEN @inicio AND @fin GROUP BY MONTH(d.fecha_corte)
                """, new { caja = cajaId, inicio, fin }, ct);
            var cartera = (await db.ConsultarAsync<FilaCartera>(
                """
                SELECT CAST(COALESCE(SUM(saldo_vigente), 0) AS DECIMAL(14,2)) AS Saldo, COUNT(*) AS Cuenta
                  FROM v_prestamo_resumen WHERE caja_id = @caja AND estado = 'VIGENTE' AND saldo_vigente > 0
                """, new { caja = cajaId }, ct)).Single();
            var ciclo = (await db.ConsultarAsync<FilaCicloAnio>(
                """
                SELECT c.estado AS Estado,
                       CAST((SELECT COALESCE(SUM(d.interes_asignado), 0) FROM ciclo_distribucion d WHERE d.ciclo_id = c.id) AS DECIMAL(14,2)) AS Distribuido
                  FROM ciclo c WHERE c.caja_id = @caja AND c.anio = @anio
                """, new { caja = cajaId, anio }, ct)).FirstOrDefault();

            var meses = Enumerable.Range(1, 12).Select(m => new ResultadoMesDto(
                m, cobrados.FirstOrDefault(x => x.Mes == m)?.Monto ?? 0m, devengados.FirstOrDefault(x => x.Mes == m)?.Monto ?? 0m)).ToList();
            var totalCobrado = meses.Sum(m => m.Cobrados);
            var cerrado = ciclo?.Estado == "CERRADO";
            return new EstadoResultadosDto(
                cajaId, nombre, anio, ciclo is null ? null : ConversionesEnum.DeDb<EstadoCiclo>(ciclo.Estado), totalCobrado,
                meses.Sum(m => m.Devengados), totalCobrado, cerrado ? ciclo!.Distribuido : 0m, cartera.Saldo, (int)cartera.Cuenta, meses);
        });
    }

    private static readonly string[] NombresMes =
        ["Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"];

    private static TablaReporte Tabla(EstadoResultadosDto e)
    {
        var estado = e.EstadoCiclo is null ? "sin ciclo" : e.EstadoCiclo.Value switch
        {
            EstadoCiclo.Abierto => "abierto", EstadoCiclo.EnCierre => "en cierre", _ => "cerrado"
        };
        return new TablaReporte(
            $"Estado de resultados {e.Anio}", $"{e.Caja} · ciclo {estado} · importes en MXN",
            [
                ("Intereses cobrados", Dinero(e.InteresesCobrados)),
                ("Intereses devengados", Dinero(e.InteresesDevengados)),
                ("Utilidad a distribuir", Dinero(e.UtilidadADistribuir)),
                ("Interés ya distribuido", Dinero(e.InteresDistribuido)),
                ("Saldo de cartera vigente", Dinero(e.SaldoCartera)),
                ("Préstamos vigentes", e.PrestamosVigentes.ToString(Inv))
            ],
            [new("Mes", TipoColumna.Texto, 2), new("Intereses cobrados", TipoColumna.Dinero), new("Intereses devengados", TipoColumna.Dinero)],
            e.Meses.Select(m => new object?[] { NombresMes[m.Mes - 1], m.Cobrados, m.Devengados }).ToList(),
            ["Total", e.InteresesCobrados, e.InteresesDevengados]);
    }

    public async Task<ArchivoGenerado> EstadoResultadosArchivoAsync(int cajaId, int anio, FormatoReporte formato, CancellationToken ct = default)
    {
        var e = await EstadoResultadosAsync(cajaId, anio, ct);
        return Empaquetar($"estado-resultados-{e.Caja}-{anio}", formato, () => Tabla(e), e, e.Meses.Count, e.UtilidadADistribuir);
    }

    // ---------- Estado de cuenta ----------

    private static readonly Dictionary<TipoMovimiento, string> EtiquetaTipo = new()
    {
        [TipoMovimiento.AhorroOrdinario] = "Ahorro ordinario",
        [TipoMovimiento.AhorroVoluntario] = "Ahorro voluntario",
        [TipoMovimiento.DesembolsoPrestamo] = "Desembolso de préstamo",
        [TipoMovimiento.AbonoCapital] = "Abono a capital",
        [TipoMovimiento.AbonoInteres] = "Abono de interés",
        [TipoMovimiento.InteresAnual] = "Rendimiento anual",
        [TipoMovimiento.LiquidacionCiclo] = "Liquidación de ahorro"
    };

    public async Task<ArchivoGenerado> EstadoCuentaArchivoAsync(int ahorradorId, int anio, FormatoReporte formato, CancellationToken ct = default)
    {
        var e = await ahorradores.EstadoCuentaAsync(ahorradorId, anio, ct);
        TablaReporte Armar() => new(
            $"Estado de cuenta {anio}", $"{e.NumEmpleado} · {e.Nombre} · {e.Caja} · importes en MXN",
            [
                ("Saldo de ahorro al inicio del año", Dinero(e.SaldoInicial)),
                ("Aportaciones ordinarias", Dinero(e.Totales.AportacionesOrdinarias)),
                ("Aportaciones voluntarias", Dinero(e.Totales.AportacionesVoluntarias)),
                ("Liquidaciones", Dinero(e.Totales.Liquidaciones)),
                ("Préstamos desembolsados", Dinero(e.Totales.Desembolsos)),
                ("Abonos a capital", Dinero(e.Totales.AbonosCapital)),
                ("Abonos de interés", Dinero(e.Totales.AbonosInteres)),
                ("Rendimiento anual", Dinero(e.Totales.RendimientoAnual)),
                ("Saldo de ahorro al final del año", Dinero(e.SaldoFinal))
            ],
            [new("Fecha", TipoColumna.Fecha), new("Concepto", TipoColumna.Texto, 2.2f), new("Referencia", TipoColumna.Texto, 1.4f),
             new("Importe", TipoColumna.Dinero), new("Saldo de ahorro", TipoColumna.Dinero)],
            e.Movimientos.Select(m => new object?[] { m.Fecha, EtiquetaTipo[m.Tipo], m.Referencia ?? "", m.Monto, m.SaldoAhorro }).ToList());
        return Empaquetar($"estado-cuenta-{e.NumEmpleado}-{anio}", formato, Armar, e, e.Movimientos.Count, e.SaldoFinal);
    }

    // ---------- Distribución de intereses ----------

    public async Task<ArchivoGenerado> DistribucionArchivoAsync(int cicloId, FormatoReporte formato, CancellationToken ct = default)
    {
        var resumen = await ciclos.ResumenAsync(cicloId, ct);
        var filas = await ciclos.DistribucionCompletaAsync(cicloId, ct);
        TablaReporte Armar() => new(
            $"Distribución de intereses {resumen.Ciclo.Anio}",
            $"{resumen.Ciclo.Caja} · ciclo {resumen.Ciclo.Estado.ToString().ToLowerInvariant()} · importes en MXN",
            [
                ("Interés total del ciclo", Dinero(resumen.Ciclo.InteresTotal)),
                ("Interés distribuido", Dinero(resumen.InteresDistribuido)),
                ("Participantes", resumen.Ciclo.Participantes.ToString(Inv)),
                ("Saldo-días total", resumen.SaldoDiasTotal.ToString("#,##0.00", Inv)),
                ("Ahorro liquidado", Dinero(resumen.AhorroLiquidado)),
                ("Ahorro retenido por préstamos", Dinero(resumen.AhorroRetenido))
            ],
            [new("Empleado", TipoColumna.Texto, 1.1f), new("Nombre", TipoColumna.Texto, 2.2f), new("Saldo-días", TipoColumna.Decimal, 1.4f),
             new("Saldo promedio", TipoColumna.Dinero, 1.2f), new("Días", TipoColumna.Entero, 0.7f), new("Participación", TipoColumna.Porcentaje, 1.2f),
             new("Interés asignado", TipoColumna.Dinero, 1.2f), new("Ahorro liquidado", TipoColumna.Dinero, 1.2f)],
            filas.Select(f => new object?[] { f.NumEmpleado, f.Nombre, f.SaldoDias, f.SaldoPromedio, f.DiasActivos, f.Participacion, f.InteresAsignado, f.AhorroLiquidado }).ToList(),
            ["Total", "", filas.Sum(f => f.SaldoDias), "", "", filas.Sum(f => f.Participacion), filas.Sum(f => f.InteresAsignado), filas.Sum(f => f.AhorroLiquidado)]);
        return Empaquetar($"distribucion-{resumen.Ciclo.Caja}-{resumen.Ciclo.Anio}", formato, Armar, new { resumen, filas }, filas.Count, resumen.InteresDistribuido);
    }

    // ---------- Común ----------

    public Task<ArchivoGenerado> GenerarAsync(ReporteTrabajoRequest r, CancellationToken ct = default) => r.Tipo switch
    {
        TipoReporte.EstadoCuenta => EstadoCuentaArchivoAsync(r.AhorradorId!.Value, r.Anio ?? DateTime.UtcNow.Year, r.Formato, ct),
        TipoReporte.EstadoResultados => EstadoResultadosArchivoAsync(r.CajaId!.Value, r.Anio ?? DateTime.UtcNow.Year, r.Formato, ct),
        _ => DistribucionArchivoAsync(r.CicloId!.Value, r.Formato, ct)
    };

    private static ArchivoGenerado Empaquetar(
        string baseNombre, FormatoReporte formato, Func<TablaReporte> tabla, object datos, int registros, decimal importe)
    {
        var nombre = new string(baseNombre.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
        return formato switch
        {
            FormatoReporte.Pdf => new ArchivoGenerado(ReportePdf.Generar(tabla()), $"{nombre}.pdf", "application/pdf", registros, importe, ""),
            FormatoReporte.Xlsx => new ArchivoGenerado(ReporteXlsx.Generar(tabla()), $"{nombre}.xlsx",
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", registros, importe, ""),
            _ => new ArchivoGenerado(JsonSerializer.SerializeToUtf8Bytes(datos, Json), $"{nombre}.json", "application/json", registros, importe, "")
        };
    }
}

using CajaComun.Application.Comun;
using CajaComun.Application.Movimientos;
using CajaComun.Application.Reportes;
using CajaComun.Infrastructure.Persistence;

namespace CajaComun.Infrastructure.Servicios;

public sealed class DashboardService(
    CajaComunDbContext db, ICajaAcceso acceso, IMovimientoService movimientos, ICacheDatos cache) : IDashboardService
{
    private sealed class FilaKpi
    {
        public decimal Fondo { get; set; }
        public long Activos { get; set; }
    }

    private sealed class FilaCartera
    {
        public decimal Saldo { get; set; }
        public long Cuenta { get; set; }
    }

    private sealed class FilaSerie
    {
        public int Anio { get; set; }
        public int Mes { get; set; }
        public decimal Ahorro { get; set; }
        public decimal Desembolsos { get; set; }
        public decimal Capital { get; set; }
    }

    public async Task<DashboardDto> ObtenerAsync(int? cajaId, CancellationToken ct = default)
    {
        IReadOnlyList<int> cajas = await acceso.CajasPermitidasAsync(ct);
        if (cajaId is { } id)
        {
            await acceso.ExigirAsync(id, ct);
            cajas = [id];
        }
        if (cajas.Count == 0) return new DashboardDto(0m, 0, 0, 0m, DateTime.UtcNow.Year, 0m, [], []);

        // Con una sola caja la versión es la suya; con varias se usa la global (cualquier cambio la invalida).
        // La llave incluye qué cajas se consultan: dos usuarios con cajas distintas no comparten resultado.
        var llave = $"dashboard:{string.Join('-', cajas.Order())}";
        var scope = cajas.Count == 1 ? cajas[0] : (int?)null;
        return await cache.ObtenerOCrearAsync(llave, scope, TimeSpan.FromMinutes(5), () => CalcularAsync(cajas, ct));
    }

    private async Task<DashboardDto> CalcularAsync(IReadOnlyList<int> cajas, CancellationToken ct)
    {
        var kpi = (await db.ConsultarAsync<FilaKpi>(
            """
            SELECT CAST(COALESCE(SUM(s.total), 0) AS DECIMAL(14,2)) AS Fondo, COUNT(*) AS Activos
              FROM ahorrador a JOIN v_saldo_ahorrador s ON s.ahorrador_id = a.id
             WHERE a.caja_id IN @cajas AND a.estado <> 'BAJA'
            """, new { cajas }, ct)).Single();
        var cartera = (await db.ConsultarAsync<FilaCartera>(
            """
            SELECT CAST(COALESCE(SUM(saldo_vigente), 0) AS DECIMAL(14,2)) AS Saldo, COUNT(*) AS Cuenta
              FROM v_prestamo_resumen WHERE caja_id IN @cajas AND estado = 'VIGENTE' AND saldo_vigente > 0
            """, new { cajas }, ct)).Single();

        // El ciclo en curso es el más reciente sin cerrar; su interés acumulado es el cobrado en ese año.
        var anio = await db.EscalarAsync<int>(
            "SELECT CAST(COALESCE(MAX(anio), YEAR(CURDATE())) AS SIGNED) FROM ciclo WHERE caja_id IN @cajas AND estado <> 'CERRADO'",
            new { cajas }, ct);
        var interes = await db.EscalarAsync<decimal>(
            """
            SELECT CAST(COALESCE(SUM(monto), 0) AS DECIMAL(14,2)) FROM movimiento
             WHERE caja_id IN @cajas AND tipo = 'ABONO_INTERES' AND fecha BETWEEN @inicio AND @fin
            """, new { cajas, inicio = new DateTime(anio, 1, 1), fin = new DateTime(anio, 12, 31) }, ct);

        // La gráfica cubre los 12 meses que terminan en el último movimiento registrado de las cajas.
        var ultimo = await db.EscalarAsync<DateTime?>("SELECT MAX(fecha) FROM movimiento WHERE caja_id IN @cajas", new { cajas }, ct);
        var serie = new List<PuntoMesDto>();
        if (ultimo is { } fin12)
        {
            var desde = new DateTime(fin12.Year, fin12.Month, 1).AddMonths(-11);
            var filas = await db.ConsultarAsync<FilaSerie>(
                """
                SELECT YEAR(fecha) AS Anio, MONTH(fecha) AS Mes,
                       CAST(SUM(CASE WHEN tipo IN ('AHORRO_ORDINARIO', 'AHORRO_VOLUNTARIO') THEN monto ELSE 0 END) AS DECIMAL(14,2)) AS Ahorro,
                       CAST(SUM(CASE WHEN tipo = 'DESEMBOLSO_PRESTAMO' THEN monto ELSE 0 END) AS DECIMAL(14,2)) AS Desembolsos,
                       CAST(SUM(CASE WHEN tipo = 'ABONO_CAPITAL' THEN monto ELSE 0 END) AS DECIMAL(14,2)) AS Capital
                  FROM movimiento WHERE caja_id IN @cajas AND fecha >= @desde
                 GROUP BY YEAR(fecha), MONTH(fecha) ORDER BY YEAR(fecha), MONTH(fecha)
                """, new { cajas, desde }, ct);
            for (var m = 0; m < 12; m++)
            {
                var mes = desde.AddMonths(m);
                var f = filas.FirstOrDefault(x => x.Anio == mes.Year && x.Mes == mes.Month);
                serie.Add(new PuntoMesDto(mes.Year, mes.Month, f?.Ahorro ?? 0m, f?.Desembolsos ?? 0m, f?.Capital ?? 0m));
            }
        }

        var ultimos = await movimientos.ListarAsync(new ConsultaMovimientosCaja
        {
            CajaId = cajas.Count == 1 ? cajas[0] : null, PageSize = 8
        }, ct);

        return new DashboardDto(kpi.Fondo, (int)kpi.Activos, (int)cartera.Cuenta, cartera.Saldo, anio, interes, serie, ultimos.Items);
    }
}

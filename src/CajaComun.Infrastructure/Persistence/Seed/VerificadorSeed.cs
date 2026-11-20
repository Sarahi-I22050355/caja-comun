using CajaComun.Domain.Reglas;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace CajaComun.Infrastructure.Persistence.Seed;

public sealed record ResultadoVerificacion(string Nombre, bool Correcto, string Detalle);

internal sealed record AhorradorRef(int Id, int CajaId);
internal sealed record SaldosRef(decimal Ordinario, decimal Voluntario, decimal Total);
internal sealed record PrestamoRef(decimal Monto, int PlazoQuincenas, decimal TasaMensual, string Estado);
internal sealed record CuotaRef(decimal Capital, decimal Interes, decimal Total);
internal sealed record DescuentoRef(decimal CuotaOrdinaria, decimal CuotaVoluntaria, decimal CuotaPrestamo, decimal Total);
internal sealed record CicloRef(string Caja, decimal Total, decimal Asignado, decimal Cobrado);

/// <summary>Comprobaciones de consistencia sobre los datos sembrados; si una falla, el comando termina con error.</summary>
public sealed class VerificadorSeed(CajaComunDbContext db)
{
    public async Task<List<ResultadoVerificacion>> EjecutarAsync()
    {
        var cn = db.Database.GetDbConnection();
        if (cn.State != System.Data.ConnectionState.Open) await cn.OpenAsync();
        var r = new List<ResultadoVerificacion>();

        void Caso(string nombre, bool ok, string detalle) => r.Add(new ResultadoVerificacion(nombre, ok, detalle));
        async Task<long> Cuenta(string sql, object? p = null) => await cn.ExecuteScalarAsync<long>(sql, p);

        // --- Caso de verificación: Axel Carlin Guzmán (GIM-00123) y el préstamo FOL-2024-089
        var axel = await cn.QuerySingleOrDefaultAsync<AhorradorRef>(
            "SELECT id AS Id, caja_id AS CajaId FROM ahorrador WHERE num_empleado = 'GIM-00123'");
        if (axel is null) Caso("Axel (GIM-00123) existe", false, "No se encontró.");
        else
        {
            var s = await cn.QuerySingleAsync<SaldosRef>(
                "SELECT ordinario AS Ordinario, voluntario AS Voluntario, total AS Total FROM v_saldo_ahorrador WHERE ahorrador_id = @id",
                new { id = axel.Id });
            Caso("Axel: ahorro ordinario $42,650.00", s.Ordinario == 42_650m, $"{s.Ordinario:N2}");
            Caso("Axel: ahorro voluntario $6,000.00", s.Voluntario == 6_000m, $"{s.Voluntario:N2}");
            Caso("Axel: ahorro total $48,650.00", s.Total == 48_650m, $"{s.Total:N2}");
            Caso("Axel: límite de crédito 80% = $38,920.00", CapacidadCredito.Limite(s.Total) == 38_920m,
                $"{CapacidadCredito.Limite(s.Total):N2}");

            var p = await cn.QuerySingleOrDefaultAsync<PrestamoRef>(
                "SELECT monto AS Monto, plazo_quincenas AS PlazoQuincenas, tasa_mensual AS TasaMensual, estado AS Estado FROM prestamo WHERE folio = 'FOL-2024-089'");
            Caso("FOL-2024-089: $22,500.00, 18 quincenas, 1.50% mensual, vigente",
                p is { Monto: 22_500m, PlazoQuincenas: 18, TasaMensual: 1.50m, Estado: "VIGENTE" }, p?.ToString() ?? "no existe");

            var c6 = await cn.QuerySingleOrDefaultAsync<CuotaRef>(
                @"SELECT a.capital AS Capital, a.interes AS Interes, a.total AS Total FROM prestamo_amortizacion a
                  JOIN prestamo p ON p.id = a.prestamo_id WHERE p.folio = 'FOL-2024-089' AND a.numero = 6");
            Caso("Cuota 6: capital $1,250.00 + interés $121.88 = $1,371.88",
                c6 is { Capital: 1_250.00m, Interes: 121.88m, Total: 1_371.88m }, c6?.ToString() ?? "no existe");
            var saldoInicial6 = await cn.ExecuteScalarAsync<decimal?>(
                @"SELECT a.saldo_despues FROM prestamo_amortizacion a JOIN prestamo p ON p.id = a.prestamo_id
                  WHERE p.folio = 'FOL-2024-089' AND a.numero = 5");
            Caso("Cuota 6: saldo insoluto antes = $16,250.00", saldoInicial6 == 16_250m, $"{saldoInicial6:N2}");
            var filas = await Cuenta("SELECT COUNT(*) FROM prestamo_amortizacion a JOIN prestamo p ON p.id = a.prestamo_id WHERE p.folio = 'FOL-2024-089'");
            Caso("FOL-2024-089: 18 renglones de amortización", filas == 18, filas.ToString());

            var q20 = await cn.QuerySingleOrDefaultAsync<DescuentoRef>(
                @"SELECT d.cuota_ordinaria AS CuotaOrdinaria, d.cuota_voluntaria AS CuotaVoluntaria,
                         d.cuota_prestamo AS CuotaPrestamo, d.total AS Total
                    FROM v_descuento_periodo d
                    JOIN periodo p ON p.id = d.periodo_id AND p.anio = 2024 AND p.numero = 20 AND p.caja_id = @caja
                   WHERE d.ahorrador_id = @id AND d.periodo_id = p.id",
                new { caja = axel.CajaId, id = axel.Id });
            Caso("Axel Q20: 1,400.00 + 500.00 + 1,371.88 = $3,271.88",
                q20 is { CuotaOrdinaria: 1_400m, CuotaVoluntaria: 500m, CuotaPrestamo: 1_371.88m, Total: 3_271.88m }, q20?.ToString() ?? "sin renglón");
        }

        // --- Ciclo 2023: la distribución cuadra al centavo con el interés cobrado
        foreach (var c in await cn.QueryAsync<CicloRef>(
            @"SELECT ca.nombre AS Caja, ci.interes_total AS Total,
                     (SELECT COALESCE(SUM(interes_asignado), 0) FROM ciclo_distribucion WHERE ciclo_id = ci.id) AS Asignado,
                     (SELECT COALESCE(SUM(monto), 0) FROM movimiento
                       WHERE caja_id = ca.id AND tipo = 'ABONO_INTERES' AND fecha BETWEEN '2023-01-01' AND '2023-12-31') AS Cobrado
                FROM ciclo ci JOIN caja ca ON ca.id = ci.caja_id WHERE ci.anio = 2023"))
            Caso($"Ciclo 2023 {c.Caja}: distribución = interés cobrado",
                c.Total == c.Asignado && c.Total == c.Cobrado, $"total {c.Total:N2}, asignado {c.Asignado:N2}, cobrado {c.Cobrado:N2}");

        var difInteresAnual = await Cuenta(
            @"SELECT COUNT(*) FROM ciclo_distribucion d WHERE d.interes_asignado <>
              COALESCE((SELECT SUM(m.monto) FROM movimiento m WHERE m.ahorrador_id = d.ahorrador_id AND m.tipo = 'INTERES_ANUAL' AND m.fecha = '2023-12-31'), 0)");
        Caso("Movimientos INTERES_ANUAL = distribución", difInteresAnual == 0, $"{difInteresAnual} diferencias");

        var layouts = await Cuenta("SELECT COUNT(*) FROM layout_generado WHERE tipo = 'SPEI'");
        var importeLayout = await Cuenta(
            @"SELECT COUNT(*) FROM layout_generado l WHERE l.tipo = 'SPEI' AND l.importe_total <>
              (SELECT COALESCE(SUM(d.ahorro_liquidado + d.interes_asignado), 0) FROM ciclo_distribucion d WHERE d.ciclo_id = l.ciclo_id)");
        Caso("Layouts SPEI 2023 (uno por caja) con importe = liquidación + rendimiento", layouts == 3 && importeLayout == 0,
            $"{layouts} layouts, {importeLayout} con diferencia");

        // --- Integridad
        var huerfanos = await Cuenta(
            @"SELECT (SELECT COUNT(*) FROM movimiento m JOIN ahorrador a ON a.id = m.ahorrador_id WHERE a.caja_id <> m.caja_id)
                   + (SELECT COUNT(*) FROM movimiento m WHERE m.prestamo_id IS NOT NULL
                        AND NOT EXISTS (SELECT 1 FROM prestamo p WHERE p.id = m.prestamo_id AND p.ahorrador_id = m.ahorrador_id))
                   + (SELECT COUNT(*) FROM movimiento m WHERE m.periodo_id IS NOT NULL
                        AND NOT EXISTS (SELECT 1 FROM periodo pe WHERE pe.id = m.periodo_id AND pe.caja_id = m.caja_id))");
        Caso("0 movimientos huérfanos", huerfanos == 0, $"{huerfanos}");

        var futuros = await Cuenta("SELECT COUNT(*) FROM movimiento WHERE fecha > '2024-10-31'");
        Caso("Sin movimientos posteriores a Q20 (31-oct-2024)", futuros == 0, $"{futuros}");

        var clabes = (await cn.QueryAsync<string>("SELECT clabe FROM ahorrador")).Count(c => !Clabe.EsValida(c));
        Caso("Todas las CLABE tienen dígito verificador válido", clabes == 0, $"{clabes} inválidas");

        var saldoVsFuncion = await Cuenta(
            "SELECT COUNT(*) FROM v_saldo_ahorrador v WHERE v.total <> fn_saldo_ahorro(v.ahorrador_id, '2024-10-31')");
        Caso("v_saldo_ahorrador = fn_saldo_ahorro al 31-oct-2024", saldoVsFuncion == 0, $"{saldoVsFuncion} diferencias");

        // --- Préstamos
        var capitalMal = await Cuenta(
            @"SELECT COUNT(*) FROM prestamo p WHERE p.estado <> 'RECHAZADO' AND
              ((SELECT SUM(capital) FROM prestamo_amortizacion a WHERE a.prestamo_id = p.id) <> p.monto
               OR (SELECT saldo_despues FROM prestamo_amortizacion a WHERE a.prestamo_id = p.id ORDER BY numero DESC LIMIT 1) <> 0)");
        Caso("Amortización: el capital suma el monto y el saldo final es cero", capitalMal == 0, $"{capitalMal} préstamos con error");

        var estadoMal = await Cuenta(
            @"SELECT COUNT(*) FROM v_prestamo_resumen v
               WHERE (v.estado = 'LIQUIDADO' AND v.saldo_vigente <> 0) OR (v.estado = 'VIGENTE' AND v.saldo_vigente <= 0)
                  OR (v.estado IN ('VIGENTE','LIQUIDADO') AND v.capital_pagado <> (SELECT COALESCE(SUM(a.capital), 0)
                        FROM prestamo_amortizacion a WHERE a.prestamo_id = v.prestamo_id AND a.estado = 'APLICADO'))");
        Caso("Saldo de préstamos coherente con la amortización aplicada", estadoMal == 0, $"{estadoMal} inconsistentes");

        var devengadoSinCuota = await Cuenta(
            @"SELECT COUNT(*) FROM interes_devengado d
               LEFT JOIN prestamo_amortizacion a ON a.prestamo_id = d.prestamo_id AND a.periodo_id = d.periodo_id
              WHERE a.id IS NULL OR a.interes <> d.interes");
        var cuotaSinDevengado = await Cuenta(
            @"SELECT COUNT(*) FROM prestamo_amortizacion a
               WHERE a.estado = 'APLICADO' AND NOT EXISTS
                 (SELECT 1 FROM interes_devengado d WHERE d.prestamo_id = a.prestamo_id AND d.periodo_id = a.periodo_id)");
        Caso("sp_calcular_interes_devengado coincide con la amortización aplicada",
            devengadoSinCuota == 0 && cuotaSinDevengado == 0, $"{devengadoSinCuota} devengados sin cuota, {cuotaSinDevengado} cuotas sin devengado");

        var garantia = await Cuenta(
            @"SELECT COUNT(*) FROM prestamo p
               JOIN movimiento d ON d.prestamo_id = p.id AND d.tipo = 'DESEMBOLSO_PRESTAMO'
              WHERE p.garantia = 'AHORRO_80' AND p.monto > ROUND(fn_saldo_ahorro(p.ahorrador_id, d.fecha) * 0.80, 2)");
        Caso("Garantía de ahorro: ningún préstamo excede el 80% del ahorro al desembolso", garantia == 0, $"{garantia} excedidos");

        // --- Descuento por nómina vs movimientos aplicados (Q10-2023 y Q20-2024)
        foreach (var (anio, numero) in new[] { (2023, 10), (2024, 20) })
        {
            var difs = 0L;
            foreach (var periodoId in await cn.QueryAsync<int>("SELECT id FROM periodo WHERE anio = @anio AND numero = @numero", new { anio, numero }))
                difs += await Cuenta(
                    @"SELECT COUNT(*) FROM v_descuento_periodo d
                      LEFT JOIN (SELECT ahorrador_id, SUM(monto) AS total FROM movimiento
                                  WHERE periodo_id = @p AND tipo IN ('AHORRO_ORDINARIO','AHORRO_VOLUNTARIO','ABONO_CAPITAL','ABONO_INTERES')
                                  GROUP BY ahorrador_id) m ON m.ahorrador_id = d.ahorrador_id
                      WHERE d.periodo_id = @p AND d.total <> COALESCE(m.total, 0)", new { p = periodoId });
            Caso($"v_descuento_periodo = movimientos aplicados (Q{numero}-{anio})", difs == 0, $"{difs} diferencias");
        }

        return r;
    }
}

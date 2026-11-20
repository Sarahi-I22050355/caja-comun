using CajaComun.Application.Comun;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Infrastructure.Persistence;

namespace CajaComun.Infrastructure.Servicios;

/// <summary>
/// Baja de un ahorrador (por incidencia o por archivo). Su ahorro se liquida el día de la baja, igual que en los
/// datos históricos, y deja de descontarse desde ese periodo. Quien llama guarda los cambios.
/// </summary>
internal sealed class BajaAhorrador(CajaComunDbContext db)
{
    private sealed record Bloqueo(int Id, string Codigo);
    private sealed record Saldo(int AhorradorId, decimal Total);

    /// <summary>
    /// La baja se liquida con el saldo de hoy, así que debe caer dentro del primer periodo abierto: antes habría que
    /// reabrir periodos aplicados y después el saldo aún no incluye los descuentos que faltan.
    /// </summary>
    public static string? ValidarFecha(Ahorrador a, DateOnly fecha, Periodo primerAbierto) =>
        fecha < primerAbierto.FechaInicio || fecha > primerAbierto.FechaFin || fecha < a.FechaIngreso
            ? Codigos.BajaFechaInvalida
            : null;

    /// <summary>Quién no puede darse de baja todavía: con préstamo propio sin liquidar o avalando uno.</summary>
    public async Task<IReadOnlyDictionary<int, string>> BloqueosAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<int, string>();
        var filas = await db.ConsultarAsync<Bloqueo>(
            """
            SELECT ahorrador_id AS Id, 'BAJA_CON_PRESTAMO' AS Codigo
              FROM prestamo WHERE ahorrador_id IN @ids AND estado IN ('PENDIENTE', 'VIGENTE')
            UNION
            SELECT aval_ahorrador_id, 'BAJA_CON_AVAL'
              FROM prestamo WHERE aval_ahorrador_id IN @ids AND estado IN ('PENDIENTE', 'VIGENTE')
            """, new { ids }, ct);
        // Si hay ambos motivos se informa el del préstamo propio.
        return filas.GroupBy(f => f.Id).ToDictionary(g => g.Key,
            g => g.Any(x => x.Codigo == Codigos.BajaConPrestamo) ? Codigos.BajaConPrestamo : Codigos.BajaConAval);
    }

    public async Task<IReadOnlyDictionary<int, decimal>> SaldosAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return new Dictionary<int, decimal>();
        var filas = await db.ConsultarAsync<Saldo>(
            "SELECT ahorrador_id AS AhorradorId, total AS Total FROM v_saldo_ahorrador WHERE ahorrador_id IN @ids", new { ids }, ct);
        return filas.ToDictionary(f => f.AhorradorId, f => f.Total);
    }

    /// <summary>Marca la baja y registra la liquidación del ahorro. Devuelve lo liquidado.</summary>
    public decimal Ejecutar(Ahorrador a, DateOnly fecha, decimal saldo)
    {
        a.Estado = EstadoAhorrador.Baja;
        a.FechaBaja = fecha;
        if (saldo <= 0m) return 0m;
        db.Movimientos.Add(new Movimiento
        {
            CajaId = a.CajaId, AhorradorId = a.Id, Fecha = fecha, Tipo = TipoMovimiento.LiquidacionCiclo,
            Monto = saldo, Referencia = "BAJA"
        });
        return saldo;
    }
}

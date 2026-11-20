using System.Text;
using CajaComun.Application.Comun;
using CajaComun.Application.Movimientos;
using CajaComun.Domain.Enums;
using CajaComun.Infrastructure.Persistence;
using Dapper;

namespace CajaComun.Infrastructure.Servicios;

public sealed class MovimientoService(IFabricaConexion conexiones, ICajaAcceso acceso) : IMovimientoService
{
    private sealed record Fila(
        long Id, int CajaId, string Caja, int AhorradorId, string NumEmpleado, string Ahorrador, DateTime Fecha,
        string Tipo, decimal Monto, string? Referencia, int? PeriodoId, string? FolioPrestamo);

    public async Task<Pagina<MovimientoCajaDto>> ListarAsync(ConsultaMovimientosCaja consulta, CancellationToken ct = default)
    {
        IReadOnlyCollection<int> cajas = await acceso.CajasPermitidasAsync(ct);
        if (consulta.CajaId is { } cajaId)
        {
            await acceso.ExigirAsync(cajaId, ct);
            cajas = [cajaId];
        }
        if (cajas.Count == 0) return new Pagina<MovimientoCajaDto>([], consulta.PaginaSegura, consulta.TamanoSeguro, 0);

        var where = new StringBuilder("m.caja_id IN @cajas");
        var p = new DynamicParameters();
        p.Add("cajas", cajas);
        if (consulta.AhorradorId is { } ahorrador) { where.Append(" AND m.ahorrador_id = @ahorrador"); p.Add("ahorrador", ahorrador); }
        if (consulta.Tipo is { } tipo) { where.Append(" AND m.tipo = @tipo"); p.Add("tipo", ConversionesEnum.ADb(tipo)); }
        if (consulta.Desde is { } desde) { where.Append(" AND m.fecha >= @desde"); p.Add("desde", desde); }
        if (consulta.Hasta is { } hasta) { where.Append(" AND m.fecha <= @hasta"); p.Add("hasta", hasta); }
        if (consulta.PeriodoId is { } periodo) { where.Append(" AND m.periodo_id = @periodo"); p.Add("periodo", periodo); }
        p.Add("tomar", consulta.TamanoSeguro);
        p.Add("saltar", consulta.Saltar);

        var sql = $"""
            SELECT m.id AS Id, m.caja_id AS CajaId, c.nombre AS Caja, m.ahorrador_id AS AhorradorId,
                   a.num_empleado AS NumEmpleado, CONCAT(a.nombre, ' ', a.apellidos) AS Ahorrador, m.fecha AS Fecha,
                   m.tipo AS Tipo, m.monto AS Monto, m.referencia AS Referencia, m.periodo_id AS PeriodoId,
                   pr.folio AS FolioPrestamo
              FROM movimiento m
              JOIN caja c ON c.id = m.caja_id
              JOIN ahorrador a ON a.id = m.ahorrador_id
              LEFT JOIN prestamo pr ON pr.id = m.prestamo_id
             WHERE {where}
             ORDER BY m.fecha DESC, m.id DESC
             LIMIT @tomar OFFSET @saltar
            """;

        await using var cn = await conexiones.AbrirAsync(ct);
        var total = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) FROM movimiento m WHERE {where}", p, cancellationToken: ct));
        var filas = await cn.QueryAsync<Fila>(new CommandDefinition(sql, p, cancellationToken: ct));
        var items = filas.Select(f => new MovimientoCajaDto(
            f.Id, f.CajaId, f.Caja, f.AhorradorId, f.NumEmpleado, f.Ahorrador, DateOnly.FromDateTime(f.Fecha),
            ConversionesEnum.DeDb<TipoMovimiento>(f.Tipo), f.Monto, f.Referencia, f.PeriodoId, f.FolioPrestamo)).ToList();
        return new Pagina<MovimientoCajaDto>(items, consulta.PaginaSegura, consulta.TamanoSeguro, total);
    }
}

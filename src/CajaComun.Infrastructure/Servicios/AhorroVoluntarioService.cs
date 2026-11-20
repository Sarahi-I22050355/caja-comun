using System.Text;
using CajaComun.Application.AhorroVoluntario;
using CajaComun.Application.Comun;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Infrastructure.Persistence;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace CajaComun.Infrastructure.Servicios;

public sealed class AhorroVoluntarioService(CajaComunDbContext db, ICajaAcceso acceso, TimeProvider reloj, ICacheDatos? cache = null) : IAhorroVoluntarioService
{
    private readonly ICacheDatos _cache = cache ?? new SinCache();
    private readonly CalendarioPeriodos _calendario = new(db);

    // Clase y no registro: CAST(booleano AS SIGNED) llega como entero de ancho variable.
    private sealed class Fila
    {
        public int AhorradorId { get; set; }
        public int CajaId { get; set; }
        public string NumEmpleado { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string Estado { get; set; } = "";
        public long? Activo { get; set; }
        public decimal? CuotaVoluntaria { get; set; }
        public DateTime? VigenteDesde { get; set; }
        public decimal Acumulado { get; set; }
        public decimal Total { get; set; }
    }

    // La cuota que rige es la del primer periodo abierto de la caja, que es la que se descontará en la próxima nómina.
    private const string Desde = """
          FROM ahorrador a
          LEFT JOIN cuota_ahorro cu
            ON cu.ahorrador_id = a.id
           AND cu.vigente_desde = (SELECT MAX(c2.vigente_desde) FROM cuota_ahorro c2
                                    WHERE c2.ahorrador_id = a.id
                                      AND c2.vigente_desde <= COALESCE((SELECT p.fecha_fin FROM periodo p
                                                                         WHERE p.caja_id = a.caja_id AND p.estado = 'ABIERTO'
                                                                         ORDER BY p.fecha_inicio LIMIT 1), @hoy))
          LEFT JOIN v_saldo_ahorrador s ON s.ahorrador_id = a.id
        """;

    private (StringBuilder Where, DynamicParameters Parametros) Filtro(IReadOnlyCollection<int> cajas, string? q, bool? activo)
    {
        var where = new StringBuilder("a.caja_id IN @cajas AND a.estado <> 'BAJA'");
        var p = new DynamicParameters();
        p.Add("cajas", cajas);
        p.Add("hoy", reloj.GetUtcNow().UtcDateTime.Date);
        if (!string.IsNullOrWhiteSpace(q))
        {
            where.Append(" AND (a.num_empleado LIKE @q OR a.nombre LIKE @q OR a.apellidos LIKE @q OR CONCAT(a.nombre, ' ', a.apellidos) LIKE @q)");
            p.Add("q", "%" + q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
        }
        if (activo is { } a) where.Append(a ? " AND cu.voluntario_activo = 1" : " AND (cu.voluntario_activo IS NULL OR cu.voluntario_activo = 0)");
        return (where, p);
    }

    private async Task<IReadOnlyCollection<int>> CajasAsync(int? cajaId, CancellationToken ct)
    {
        if (cajaId is { } id)
        {
            await acceso.ExigirAsync(id, ct);
            return [id];
        }
        return await acceso.CajasPermitidasAsync(ct);
    }

    private const string Columnas = """
        SELECT a.id AS AhorradorId, a.caja_id AS CajaId, a.num_empleado AS NumEmpleado,
               CONCAT(a.nombre, ' ', a.apellidos) AS Nombre, a.estado AS Estado,
               CAST(cu.voluntario_activo AS SIGNED) AS Activo, cu.cuota_voluntaria AS CuotaVoluntaria,
               cu.vigente_desde AS VigenteDesde, COALESCE(s.voluntario, 0) AS Acumulado, COALESCE(s.total, 0) AS Total
        """;

    private static VoluntarioDto Mapear(Fila f) => new(
        f.AhorradorId, f.CajaId, f.NumEmpleado, f.Nombre, ConversionesEnum.DeDb<EstadoAhorrador>(f.Estado), f.Activo is 1,
        f.Activo is 1 ? f.CuotaVoluntaria ?? 0m : 0m, f.VigenteDesde is { } d ? DateOnly.FromDateTime(d) : null,
        f.Acumulado, f.Total);

    public async Task<Pagina<VoluntarioDto>> ListarAsync(ConsultaVoluntario consulta, CancellationToken ct = default)
    {
        var cajas = await CajasAsync(consulta.CajaId, ct);
        if (cajas.Count == 0) return new Pagina<VoluntarioDto>([], consulta.PaginaSegura, consulta.TamanoSeguro, 0);

        var (where, p) = Filtro(cajas, consulta.Q, consulta.Activo);
        p.Add("tomar", consulta.TamanoSeguro);
        p.Add("saltar", consulta.Saltar);

        var total = await db.EscalarAsync<int>($"SELECT CAST(COUNT(*) AS SIGNED) {Desde} WHERE {where}", p, ct);
        var filas = await db.ConsultarAsync<Fila>(
            $"{Columnas} {Desde} WHERE {where} ORDER BY a.apellidos, a.nombre, a.id LIMIT @tomar OFFSET @saltar", p, ct);
        return new Pagina<VoluntarioDto>(filas.Select(Mapear).ToList(), consulta.PaginaSegura, consulta.TamanoSeguro, total);
    }

    private sealed record FilaResumen(long Activos, decimal Cuota, decimal Acumulado);

    public async Task<ResumenVoluntarioDto> ResumenAsync(int? cajaId, CancellationToken ct = default)
    {
        var cajas = await CajasAsync(cajaId, ct);
        if (cajas.Count == 0) return new ResumenVoluntarioDto(0, 0m, 0m);

        var (where, p) = Filtro(cajas, null, null);
        var r = (await db.ConsultarAsync<FilaResumen>(
            $"""
            SELECT CAST(COALESCE(SUM(cu.voluntario_activo = 1), 0) AS SIGNED) AS Activos,
                   CAST(COALESCE(SUM(CASE WHEN cu.voluntario_activo = 1 THEN cu.cuota_voluntaria ELSE 0 END), 0) AS DECIMAL(14,2)) AS Cuota,
                   CAST(COALESCE(SUM(s.voluntario), 0) AS DECIMAL(14,2)) AS Acumulado
            {Desde} WHERE {where}
            """, p, ct)).Single();
        return new ResumenVoluntarioDto((int)r.Activos, r.Cuota, r.Acumulado);
    }

    public async Task<VoluntarioDto> ActualizarAsync(int ahorradorId, ActualizarVoluntarioRequest request, CancellationToken ct = default)
    {
        var permitidas = await acceso.CajasPermitidasAsync(ct);
        var a = await db.Ahorradores.AsNoTracking().FirstOrDefaultAsync(x => x.Id == ahorradorId && permitidas.Contains(x.CajaId), ct)
            ?? throw ErrorNegocio.NoEncontrado(Codigos.AhorradorNoEncontrado, "El ahorrador no existe.");
        if (a.Estado == EstadoAhorrador.Baja)
            throw ErrorNegocio.Conflicto(Codigos.AhorradorEnBaja, "El ahorrador está dado de baja.");

        var abierto = await _calendario.PrimerAbiertoAsync(a.CajaId, ct)
            ?? throw ErrorNegocio.Conflicto(Codigos.SinPeriodoAbierto, "La caja no tiene un periodo abierto.");
        // Igual que cualquier cuota: no puede cambiar descuentos de periodos ya aplicados.
        var desde = request.VigenteDesde ?? abierto.FechaInicio;
        if (desde < abierto.FechaInicio || desde < a.FechaIngreso)
            throw new ErrorNegocio(Codigos.CuotaFechaInvalida,
                "La cuota debe regir desde un periodo abierto y no antes del ingreso del ahorrador.", 400,
                new Dictionary<string, string[]> { ["vigenteDesde"] = [Codigos.CuotaFechaInvalida] });

        var mismaFecha = await db.CuotasAhorro.FirstOrDefaultAsync(c => c.AhorradorId == a.Id && c.VigenteDesde == desde, ct);
        if (mismaFecha is not null)
        {
            mismaFecha.VoluntarioActivo = request.Activo;
            mismaFecha.CuotaVoluntaria = request.CuotaVoluntaria;
        }
        else
        {
            var vigente = await db.CuotasAhorro.AsNoTracking()
                .Where(c => c.AhorradorId == a.Id && c.VigenteDesde <= desde)
                .OrderByDescending(c => c.VigenteDesde).FirstOrDefaultAsync(ct);
            db.CuotasAhorro.Add(new CuotaAhorro
            {
                AhorradorId = a.Id, CuotaOrdinaria = vigente?.CuotaOrdinaria ?? 0m, VoluntarioActivo = request.Activo,
                CuotaVoluntaria = request.CuotaVoluntaria, VigenteDesde = desde
            });
        }
        await db.SaveChangesAsync(ct);
        await _cache.InvalidarAsync(a.CajaId);

        var (where, p) = Filtro([a.CajaId], null, null);
        where.Append(" AND a.id = @id");
        p.Add("id", a.Id);
        var fila = (await db.ConsultarAsync<Fila>($"{Columnas} {Desde} WHERE {where}", p, ct)).Single();
        return Mapear(fila);
    }
}

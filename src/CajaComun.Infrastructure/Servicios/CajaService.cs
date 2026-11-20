using CajaComun.Application.Cajas;
using CajaComun.Application.Comun;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;
using CajaComun.Infrastructure.Persistence;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace CajaComun.Infrastructure.Servicios;

public sealed class CajaService(CajaComunDbContext db, IFabricaConexion conexiones, ICajaAcceso acceso) : ICajaService
{
    public async Task<IReadOnlyList<CajaDto>> ListarAsync(CancellationToken ct = default)
    {
        var ids = await acceso.CajasPermitidasAsync(ct);
        return await ConsultarAsync(ids, ct);
    }

    public async Task<CajaDto> ObtenerAsync(int id, CancellationToken ct = default)
    {
        await ExigirExistenteAsync(id, ct);
        return (await ConsultarAsync([id], ct)).Single();
    }

    public async Task<CajaDto> CrearAsync(CrearCajaRequest request, CancellationToken ct = default)
    {
        var nombre = request.Nombre.Trim();
        var prefijo = request.Prefijo.Trim();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        Empresa empresa;
        if (request.EmpresaId is { } empresaId)
        {
            empresa = await db.Empresas.FirstOrDefaultAsync(e => e.Id == empresaId, ct)
                ?? throw ErrorNegocio.NoEncontrado(Codigos.EmpresaNoEncontrada, "La empresa no existe.");
        }
        else
        {
            var nombreEmpresa = request.EmpresaNueva!.Trim();
            empresa = await db.Empresas.FirstOrDefaultAsync(e => e.Nombre == nombreEmpresa, ct)
                ?? db.Empresas.Add(new Empresa { Nombre = nombreEmpresa }).Entity;
        }

        if (empresa.Id != 0 && await db.Cajas.AnyAsync(c => c.EmpresaId == empresa.Id && c.Nombre == nombre, ct))
            throw ErrorNegocio.Conflicto(Codigos.CajaDuplicada, "La empresa ya tiene una caja con ese nombre.");
        if (await db.Cajas.AnyAsync(c => c.Prefijo == prefijo, ct))
            throw ErrorNegocio.Conflicto(Codigos.CajaDuplicada, "Ese prefijo de número de empleado ya lo usa otra caja.");

        var caja = new Caja
        {
            Empresa = empresa,
            Nombre = nombre,
            Prefijo = prefijo,
            TasaInteresMensual = request.TasaInteresMensual,
            FrecuenciaNomina = request.FrecuenciaNomina,
            InicioCiclo = new DateOnly(request.AnioCiclo, 1, 1),
            FinCiclo = new DateOnly(request.AnioCiclo, 12, 31)
        };
        db.Cajas.Add(caja);

        // La caja nace con los periodos de nómina de su ciclo, todos abiertos.
        foreach (var p in PeriodosCiclo.Generar(request.FrecuenciaNomina, request.AnioCiclo))
            db.Periodos.Add(new Periodo
            {
                Caja = caja, Anio = request.AnioCiclo, Numero = p.Numero, FechaInicio = p.Inicio, FechaFin = p.Fin
            });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await ConsultarAsync([caja.Id], ct)).Single();
    }

    public async Task<CajaDto> ActualizarAsync(int id, ActualizarCajaRequest request, CancellationToken ct = default)
    {
        await ExigirExistenteAsync(id, ct);
        var caja = await db.Cajas.FirstAsync(c => c.Id == id, ct);
        var nombre = request.Nombre.Trim();

        if (await db.Cajas.AnyAsync(c => c.EmpresaId == caja.EmpresaId && c.Nombre == nombre && c.Id != id, ct))
            throw ErrorNegocio.Conflicto(Codigos.CajaDuplicada, "La empresa ya tiene una caja con ese nombre.");

        // La tasa nueva solo aplica a préstamos futuros: cada préstamo guarda la tasa con la que se otorgó.
        caja.Nombre = nombre;
        caja.TasaInteresMensual = request.TasaInteresMensual;
        caja.Activa = request.Activa;
        await db.SaveChangesAsync(ct);
        return (await ConsultarAsync([id], ct)).Single();
    }

    public async Task<IReadOnlyList<EmpresaDto>> ListarEmpresasAsync(CancellationToken ct = default) =>
        await db.Empresas.AsNoTracking().OrderBy(e => e.Nombre)
            .Select(e => new EmpresaDto(e.Id, e.Nombre, e.Activa)).ToListAsync(ct);

    public async Task<IReadOnlyList<PeriodoDto>> ListarPeriodosAsync(int cajaId, int? anio, CancellationToken ct = default)
    {
        await ExigirExistenteAsync(cajaId, ct);
        var consulta = db.Periodos.AsNoTracking().Where(p => p.CajaId == cajaId);
        if (anio is { } a) consulta = consulta.Where(p => p.Anio == a);
        return await consulta.OrderBy(p => p.Anio).ThenBy(p => p.Numero)
            .Select(p => new PeriodoDto(p.Id, p.Anio, p.Numero, p.FechaInicio, p.FechaFin, p.Estado))
            .ToListAsync(ct);
    }

    // Una caja ajena responde 403; una caja que no existe, 404. Primero el permiso, para no revelar cuáles existen.
    private async Task ExigirExistenteAsync(int id, CancellationToken ct)
    {
        await acceso.ExigirAsync(id, ct);
        if (!await db.Cajas.AnyAsync(c => c.Id == id, ct))
            throw ErrorNegocio.NoEncontrado(Codigos.CajaNoEncontrada, "La caja no existe.");
    }

    private sealed record FilaCaja(
        int Id, int EmpresaId, string Empresa, string Nombre, string Prefijo, decimal TasaInteresMensual,
        string FrecuenciaNomina, DateTime InicioCiclo, DateTime FinCiclo, bool Activa, long AhorradoresActivos, decimal AhorroTotal);

    private async Task<IReadOnlyList<CajaDto>> ConsultarAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return [];

        // Una sola consulta con agregados por caja: sin consultas por renglón.
        const string sql = """
            SELECT c.id AS Id, c.empresa_id AS EmpresaId, e.nombre AS Empresa, c.nombre AS Nombre, c.prefijo AS Prefijo,
                   c.tasa_interes_mensual AS TasaInteresMensual, c.frecuencia_nomina AS FrecuenciaNomina,
                   c.inicio_ciclo AS InicioCiclo, c.fin_ciclo AS FinCiclo, c.activa AS Activa,
                   CAST(COALESCE(x.activos, 0) AS SIGNED) AS AhorradoresActivos, CAST(COALESCE(x.ahorro, 0) AS DECIMAL(14,2)) AS AhorroTotal
              FROM caja c
              JOIN empresa e ON e.id = c.empresa_id
              LEFT JOIN (SELECT a.caja_id,
                                SUM(a.estado <> 'BAJA') AS activos,
                                SUM(s.total) AS ahorro
                           FROM ahorrador a
                           JOIN v_saldo_ahorrador s ON s.ahorrador_id = a.id
                          WHERE a.caja_id IN @ids
                          GROUP BY a.caja_id) x ON x.caja_id = c.id
             WHERE c.id IN @ids
             ORDER BY e.nombre, c.nombre
            """;
        await using var cn = await conexiones.AbrirAsync(ct);
        var filas = await cn.QueryAsync<FilaCaja>(new CommandDefinition(sql, new { ids }, cancellationToken: ct));
        return filas.Select(f => new CajaDto(
                f.Id, f.EmpresaId, f.Empresa, f.Nombre, f.Prefijo, f.TasaInteresMensual,
                ConversionesEnum.DeDb<FrecuenciaNomina>(f.FrecuenciaNomina),
                DateOnly.FromDateTime(f.InicioCiclo), DateOnly.FromDateTime(f.FinCiclo), f.Activa,
                (int)f.AhorradoresActivos, f.AhorroTotal))
            .ToList();
    }
}

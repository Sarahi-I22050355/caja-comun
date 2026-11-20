using System.Text;
using CajaComun.Application.Ciclos;
using CajaComun.Application.Comun;
using CajaComun.Application.Nomina;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;
using CajaComun.Infrastructure.Persistence;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace CajaComun.Infrastructure.Servicios;

public sealed class CicloService(
    CajaComunDbContext db, ICajaAcceso acceso, IUsuarioActual usuario, TimeProvider reloj,
    IEnumerable<ILayoutBancario> layouts, ICacheDatos? cache = null) : ICicloService
{
    private readonly ICacheDatos _cache = cache ?? new SinCache();

    // ---------- Consulta ----------

    private sealed class FilaCiclo
    {
        public int Id { get; set; }
        public int CajaId { get; set; }
        public string Caja { get; set; } = "";
        public int Anio { get; set; }
        public string Estado { get; set; } = "";
        public decimal InteresTotal { get; set; }
        public DateTime? FechaCierre { get; set; }
        public long Participantes { get; set; }
    }

    private const string SelectCiclo = """
        SELECT c.id AS Id, c.caja_id AS CajaId, ca.nombre AS Caja, c.anio AS Anio, c.estado AS Estado,
               c.interes_total AS InteresTotal, c.fecha_cierre AS FechaCierre,
               (SELECT COUNT(*) FROM ciclo_distribucion d WHERE d.ciclo_id = c.id) AS Participantes
          FROM ciclo c
          JOIN caja ca ON ca.id = c.caja_id
        """;

    private static CicloDto Mapear(FilaCiclo f) => new(
        f.Id, f.CajaId, f.Caja, f.Anio, ConversionesEnum.DeDb<EstadoCiclo>(f.Estado), f.InteresTotal,
        f.FechaCierre is { } fc ? DateTime.SpecifyKind(fc, DateTimeKind.Utc) : null, (int)f.Participantes);

    /// <summary>Un ciclo de una caja ajena responde 404, igual que uno inexistente.</summary>
    private async Task<FilaCiclo> CargarAsync(int id, CancellationToken ct)
    {
        var permitidas = await acceso.CajasPermitidasAsync(ct);
        return (await db.ConsultarAsync<FilaCiclo>(
            $"{SelectCiclo} WHERE c.id = @id AND c.caja_id IN @permitidas", new { id, permitidas }, ct)).FirstOrDefault()
            ?? throw ErrorNegocio.NoEncontrado(Codigos.CicloNoEncontrado, "El ciclo no existe.");
    }

    public async Task<IReadOnlyList<CicloDto>> ListarAsync(int? cajaId, CancellationToken ct = default)
    {
        IReadOnlyCollection<int> cajas = await acceso.CajasPermitidasAsync(ct);
        if (cajaId is { } id)
        {
            await acceso.ExigirAsync(id, ct);
            cajas = [id];
        }
        if (cajas.Count == 0) return [];
        var filas = await db.ConsultarAsync<FilaCiclo>(
            $"{SelectCiclo} WHERE c.caja_id IN @cajas ORDER BY ca.nombre, c.anio DESC", new { cajas }, ct);
        return filas.Select(Mapear).ToList();
    }

    private sealed class FilaResumen
    {
        public decimal SaldoDias { get; set; }
        public decimal Interes { get; set; }
        public decimal Liquidado { get; set; }
        public decimal Retenido { get; set; }
    }

    public async Task<CicloResumenDto> ResumenAsync(int id, CancellationToken ct = default)
    {
        var c = await CargarAsync(id, ct);
        return await ArmarResumenAsync(c, ct);
    }

    private async Task<CicloResumenDto> ArmarResumenAsync(FilaCiclo c, CancellationToken ct)
    {
        var inicio = new DateTime(c.Anio, 1, 1);
        var fin = new DateTime(c.Anio, 12, 31);
        var cobrado = await db.EscalarAsync<decimal>(
            """
            SELECT CAST(COALESCE(SUM(monto), 0) AS DECIMAL(14,2)) FROM movimiento
             WHERE caja_id = @caja AND tipo = 'ABONO_INTERES' AND fecha BETWEEN @inicio AND @fin
            """, new { caja = c.CajaId, inicio, fin }, ct);
        var devengado = await db.EscalarAsync<decimal>(
            """
            SELECT CAST(COALESCE(SUM(d.interes), 0) AS DECIMAL(14,2))
              FROM interes_devengado d JOIN prestamo p ON p.id = d.prestamo_id
             WHERE p.caja_id = @caja AND d.fecha_corte BETWEEN @inicio AND @fin
            """, new { caja = c.CajaId, inicio, fin }, ct);
        var periodos = await db.EscalarAsync<string>(
            "SELECT CONCAT(COUNT(*), ',', COALESCE(SUM(estado <> 'ABIERTO'), 0)) FROM periodo WHERE caja_id = @caja AND anio = @anio",
            new { caja = c.CajaId, anio = c.Anio }, ct);
        var vigentes = await db.EscalarAsync<int>(
            "SELECT CAST(COUNT(*) AS SIGNED) FROM v_prestamo_resumen WHERE caja_id = @caja AND estado = 'VIGENTE' AND saldo_vigente > 0",
            new { caja = c.CajaId }, ct);
        var dist = (await db.ConsultarAsync<FilaResumen>(
            """
            SELECT CAST(COALESCE(SUM(saldo_dias), 0) AS DECIMAL(18,2)) AS SaldoDias,
                   CAST(COALESCE(SUM(interes_asignado), 0) AS DECIMAL(14,2)) AS Interes,
                   CAST(COALESCE(SUM(ahorro_liquidado), 0) AS DECIMAL(14,2)) AS Liquidado,
                   CAST(COALESCE(SUM(ahorro_retenido), 0) AS DECIMAL(14,2)) AS Retenido
              FROM ciclo_distribucion WHERE ciclo_id = @id
            """, new { id = c.Id }, ct)).Single();

        var partes = periodos.Split(',');
        var totales = int.Parse(partes[0]);
        var aplicados = int.Parse(partes[1]);
        var impedimentos = new List<string>();
        var estado = ConversionesEnum.DeDb<EstadoCiclo>(c.Estado);
        if (estado == EstadoCiclo.Cerrado) impedimentos.Add(Codigos.CicloYaCerrado);
        else
        {
            if (totales == 0) impedimentos.Add("CICLO_SIN_PERIODOS");
            else if (aplicados < totales) impedimentos.Add($"{Codigos.CicloPeriodosAbiertos}:{totales - aplicados}");
        }

        return new CicloResumenDto(
            Mapear(c), cobrado, devengado, totales, aplicados, vigentes, dist.SaldoDias, dist.Interes, dist.Liquidado,
            dist.Retenido, impedimentos.Count == 0, impedimentos);
    }

    private sealed class FilaDistribucion
    {
        public int AhorradorId { get; set; }
        public string NumEmpleado { get; set; } = "";
        public string Nombre { get; set; } = "";
        public decimal SaldoDias { get; set; }
        public decimal SaldoPromedio { get; set; }
        public int DiasActivos { get; set; }
        public decimal Participacion { get; set; }
        public decimal InteresAsignado { get; set; }
        public decimal AhorroLiquidado { get; set; }
        public decimal AhorroRetenido { get; set; }
    }

    public async Task<Pagina<DistribucionDto>> DistribucionAsync(int id, ConsultaDistribucion consulta, CancellationToken ct = default)
    {
        await CargarAsync(id, ct);
        var where = new StringBuilder("d.ciclo_id = @id");
        var p = new DynamicParameters();
        p.Add("id", id);
        if (!string.IsNullOrWhiteSpace(consulta.Q))
        {
            where.Append(" AND (a.num_empleado LIKE @q OR a.nombre LIKE @q OR a.apellidos LIKE @q OR CONCAT(a.nombre, ' ', a.apellidos) LIKE @q)");
            p.Add("q", "%" + consulta.Q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
        }
        p.Add("tomar", consulta.TamanoSeguro);
        p.Add("saltar", consulta.Saltar);

        var total = await db.EscalarAsync<int>(
            $"SELECT CAST(COUNT(*) AS SIGNED) FROM ciclo_distribucion d JOIN ahorrador a ON a.id = d.ahorrador_id WHERE {where}", p, ct);
        var filas = await db.ConsultarAsync<FilaDistribucion>(
            $"""
            SELECT d.ahorrador_id AS AhorradorId, a.num_empleado AS NumEmpleado, CONCAT(a.nombre, ' ', a.apellidos) AS Nombre,
                   d.saldo_dias AS SaldoDias, d.saldo_promedio AS SaldoPromedio, d.dias_activos AS DiasActivos,
                   d.participacion AS Participacion, d.interes_asignado AS InteresAsignado,
                   d.ahorro_liquidado AS AhorroLiquidado, d.ahorro_retenido AS AhorroRetenido
              FROM ciclo_distribucion d
              JOIN ahorrador a ON a.id = d.ahorrador_id
             WHERE {where}
             ORDER BY d.saldo_dias DESC, d.ahorrador_id
             LIMIT @tomar OFFSET @saltar
            """, p, ct);
        return new Pagina<DistribucionDto>(filas.Select(Mapear).ToList(), consulta.PaginaSegura, consulta.TamanoSeguro, total);
    }

    public async Task<IReadOnlyList<DistribucionDto>> DistribucionCompletaAsync(int id, CancellationToken ct = default)
    {
        await CargarAsync(id, ct);
        var filas = await db.ConsultarAsync<FilaDistribucion>(
            """
            SELECT d.ahorrador_id AS AhorradorId, a.num_empleado AS NumEmpleado, CONCAT(a.nombre, ' ', a.apellidos) AS Nombre,
                   d.saldo_dias AS SaldoDias, d.saldo_promedio AS SaldoPromedio, d.dias_activos AS DiasActivos,
                   d.participacion AS Participacion, d.interes_asignado AS InteresAsignado,
                   d.ahorro_liquidado AS AhorroLiquidado, d.ahorro_retenido AS AhorroRetenido
              FROM ciclo_distribucion d JOIN ahorrador a ON a.id = d.ahorrador_id
             WHERE d.ciclo_id = @id
             ORDER BY a.num_empleado
            """, new { id }, ct);
        return filas.Select(Mapear).ToList();
    }

    private static DistribucionDto Mapear(FilaDistribucion f) => new(
        f.AhorradorId, f.NumEmpleado, f.Nombre, f.SaldoDias, f.SaldoPromedio, f.DiasActivos, f.Participacion, f.InteresAsignado,
        f.AhorroLiquidado, f.AhorroRetenido);

    // ---------- Borrador ----------

    public async Task<CicloResumenDto> BorradorAsync(BorradorCicloRequest request, CancellationToken ct = default)
    {
        await acceso.ExigirAsync(request.CajaId, ct);
        if (!await db.Cajas.AnyAsync(c => c.Id == request.CajaId, ct))
            throw ErrorNegocio.NoEncontrado(Codigos.CajaNoEncontrada, "La caja no existe.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var existente = await db.Ciclos.FirstOrDefaultAsync(c => c.CajaId == request.CajaId && c.Anio == request.Anio, ct);
        if (existente is null)
        {
            existente = new Ciclo { CajaId = request.CajaId, Anio = request.Anio, Estado = EstadoCiclo.Abierto };
            db.Ciclos.Add(existente);
            await db.SaveChangesAsync(ct);
        }
        await BloquearAsync(existente.Id, ct);
        await RecalcularAsync(existente.Id, request.CajaId, request.Anio, ct);
        await db.EjecutarAsync("UPDATE ciclo SET estado = 'EN_CIERRE' WHERE id = @id", new { id = existente.Id }, ct);
        await tx.CommitAsync(ct);
        return await ResumenAsync(existente.Id, ct);
    }

    /// <summary>Bloquea el ciclo hasta el fin de la transacción y rechaza uno ya cerrado.</summary>
    private async Task BloquearAsync(int id, CancellationToken ct)
    {
        var estado = await db.EscalarAsync<string>("SELECT estado FROM ciclo WHERE id = @id FOR UPDATE", new { id }, ct);
        if (estado == "CERRADO")
            throw ErrorNegocio.Conflicto(Codigos.CicloYaCerrado, "El ciclo ya está cerrado y no se puede modificar.");
    }

    private sealed class FilaSaldoDias
    {
        public int AhorradorId { get; set; }
        public decimal SaldoDias { get; set; }
        public decimal AlCorte { get; set; }
    }

    private sealed class FilaSaldoPrestamo
    {
        public int AhorradorId { get; set; }
        public decimal Saldo { get; set; }
    }

    /// <summary>
    /// Recalcula la distribución del ciclo: saldo-días con el procedimiento almacenado, el interés total (intereses de
    /// préstamo cobrados en el año), el reparto por mayor residuo y el ahorro que se retiene por préstamos vigentes.
    /// </summary>
    private async Task RecalcularAsync(int cicloId, int cajaId, int anio, CancellationToken ct)
    {
        await db.EjecutarAsync("CALL sp_saldo_dias_ciclo(@id)", new { id = cicloId }, ct);

        var interes = await db.EscalarAsync<decimal>(
            """
            SELECT CAST(COALESCE(SUM(monto), 0) AS DECIMAL(14,2)) FROM movimiento
             WHERE caja_id = @caja AND tipo = 'ABONO_INTERES' AND fecha BETWEEN @inicio AND @fin
            """, new { caja = cajaId, inicio = new DateTime(anio, 1, 1), fin = new DateTime(anio, 12, 31) }, ct);

        var filas = await db.ConsultarAsync<FilaSaldoDias>(
            """
            SELECT ahorrador_id AS AhorradorId, saldo_dias AS SaldoDias, ahorro_liquidado AS AlCorte
              FROM ciclo_distribucion WHERE ciclo_id = @id ORDER BY ahorrador_id
            """, new { id = cicloId }, ct);
        var reparto = DistribucionProporcional.Repartir(interes, filas.Select(f => f.SaldoDias).ToList());

        var saldos = (await db.ConsultarAsync<FilaSaldoPrestamo>(
            """
            SELECT ahorrador_id AS AhorradorId, CAST(SUM(saldo_vigente) AS DECIMAL(14,2)) AS Saldo
              FROM v_prestamo_resumen WHERE caja_id = @caja AND estado = 'VIGENTE' AND saldo_vigente > 0 GROUP BY ahorrador_id
            """, new { caja = cajaId }, ct)).ToDictionary(f => f.AhorradorId, f => f.Saldo);

        var entidades = await db.CicloDistribuciones.Where(d => d.CicloId == cicloId).ToListAsync(ct);
        var porAhorrador = entidades.ToDictionary(d => d.AhorradorId);
        for (var i = 0; i < filas.Count; i++)
        {
            var d = porAhorrador[filas[i].AhorradorId];
            var retenido = ReglasCiclo.AhorroRetenido(filas[i].AlCorte, saldos.GetValueOrDefault(d.AhorradorId));
            d.InteresAsignado = reparto[i];
            d.AhorroRetenido = retenido;
            d.AhorroLiquidado = filas[i].AlCorte - retenido;
        }
        var ciclo = await db.Ciclos.FirstAsync(c => c.Id == cicloId, ct);
        ciclo.InteresTotal = interes;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    // ---------- Cierre ----------

    public async Task<CierreCicloResultadoDto> CerrarAsync(int id, CancellationToken ct = default)
    {
        var c = await CargarAsync(id, ct);
        if (c.Estado == "CERRADO")
            throw ErrorNegocio.Conflicto(Codigos.CicloYaCerrado, "El ciclo ya está cerrado.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        // El bloqueo serializa los cierres simultáneos: el segundo encuentra el ciclo cerrado y recibe el conflicto.
        await BloquearAsync(id, ct);

        var abiertos = await db.EscalarAsync<int>(
            "SELECT CAST(COUNT(*) AS SIGNED) FROM periodo WHERE caja_id = @caja AND anio = @anio AND estado = 'ABIERTO'",
            new { caja = c.CajaId, anio = c.Anio }, ct);
        var totales = await db.EscalarAsync<int>(
            "SELECT CAST(COUNT(*) AS SIGNED) FROM periodo WHERE caja_id = @caja AND anio = @anio", new { caja = c.CajaId, anio = c.Anio }, ct);
        if (totales == 0 || abiertos > 0)
            throw ErrorNegocio.Conflicto(Codigos.CicloPeriodosAbiertos,
                totales == 0 ? "La caja no tiene periodos en ese ciclo." : $"Faltan {abiertos} periodos del ciclo por aplicar.");

        await RecalcularAsync(id, c.CajaId, c.Anio, ct);

        var filas = await db.CicloDistribuciones.Where(d => d.CicloId == id).ToListAsync(ct);
        var fin = new DateOnly(c.Anio, 12, 31);
        var inicioSiguiente = new DateOnly(c.Anio + 1, 1, 1);
        var movimientos = 0;
        foreach (var d in filas)
        {
            void Agregar(TipoMovimiento tipo, decimal monto, DateOnly fecha, string referencia)
            {
                if (monto <= 0m) return;
                db.Movimientos.Add(new Movimiento
                {
                    CajaId = c.CajaId, AhorradorId = d.AhorradorId, Fecha = fecha, Tipo = tipo, Monto = monto, Referencia = referencia
                });
                movimientos++;
            }
            Agregar(TipoMovimiento.InteresAnual, d.InteresAsignado, fin, $"CIERRE-{c.Anio}");
            // Se liquida todo el ahorro; lo retenido regresa como aportación inicial del ciclo siguiente (igual que la
            // aportación inicial de 2024), de modo que el saldo no se duplica y el ahorro retenido sigue ganando.
            Agregar(TipoMovimiento.LiquidacionCiclo, d.AhorroLiquidado + d.AhorroRetenido, fin, $"CIERRE-{c.Anio}");
            Agregar(TipoMovimiento.AhorroOrdinario, d.AhorroRetenido, inicioSiguiente, $"SALDO-INICIAL-{c.Anio + 1}");
        }

        await db.EjecutarAsync(
            "UPDATE periodo SET estado = 'CERRADO' WHERE caja_id = @caja AND anio = @anio AND estado = 'APLICADO'",
            new { caja = c.CajaId, anio = c.Anio }, ct);
        await db.EjecutarAsync(
            "UPDATE ciclo SET estado = 'CERRADO', fecha_cierre = @ahora WHERE id = @id",
            new { id, ahora = reloj.GetUtcNow().UtcDateTime }, ct);
        if (!await db.Ciclos.AnyAsync(x => x.CajaId == c.CajaId && x.Anio == c.Anio + 1, ct))
            db.Ciclos.Add(new Ciclo { CajaId = c.CajaId, Anio = c.Anio + 1, Estado = EstadoCiclo.Abierto });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await _cache.InvalidarAsync(c.CajaId);

        return new CierreCicloResultadoDto(
            id, c.Anio, filas.Sum(d => d.InteresAsignado), filas.Count, filas.Sum(d => d.AhorroLiquidado),
            filas.Sum(d => d.AhorroRetenido), movimientos);
    }

    // ---------- Dispersión SPEI ----------

    private sealed class FilaDispersion
    {
        public int AhorradorId { get; set; }
        public string NumEmpleado { get; set; } = "";
        public string Nombre { get; set; } = "";
        public string Clabe { get; set; } = "";
        public decimal Liquidado { get; set; }
        public decimal Interes { get; set; }
    }

    public async Task<ArchivoGenerado> GenerarSpeiAsync(int id, FormatoSpei formato, bool incluirAhorro, CancellationToken ct = default)
    {
        var c = await CargarAsync(id, ct);
        if (c.Estado != "CERRADO")
            throw ErrorNegocio.Conflicto(Codigos.CicloNoCerrado, "El archivo de dispersión solo se genera de un ciclo cerrado.");

        var filas = await db.ConsultarAsync<FilaDispersion>(
            """
            SELECT d.ahorrador_id AS AhorradorId, a.num_empleado AS NumEmpleado, CONCAT(a.nombre, ' ', a.apellidos) AS Nombre,
                   a.clabe AS Clabe, d.ahorro_liquidado AS Liquidado, d.interes_asignado AS Interes
              FROM ciclo_distribucion d JOIN ahorrador a ON a.id = d.ahorrador_id
             WHERE d.ciclo_id = @id
             ORDER BY a.num_empleado
            """, new { id }, ct);

        var dispersiones = filas
            .Select(f => new DispersionSpei(f.AhorradorId, f.NumEmpleado, f.Nombre, f.Clabe,
                ReglasCiclo.ImporteDispersion(f.Liquidado, f.Interes, incluirAhorro)))
            .Where(d => d.Importe > 0m)
            .ToList();

        var layout = layouts.First(l => l.Formato == formato);
        var prefijo = await db.Cajas.AsNoTracking().Where(x => x.Id == c.CajaId).Select(x => x.Prefijo).FirstAsync(ct);
        // El banco aplica el archivo en el siguiente día hábil a su generación, nunca antes del cierre del ciclo.
        var cierre = DateOnly.FromDateTime(c.FechaCierre ?? new DateTime(c.Anio, 12, 31));
        var hoy = DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);
        var archivo = layout.Generar(dispersiones, c.Anio, ReglasCiclo.SiguienteDiaHabil(hoy > cierre ? hoy : cierre), incluirAhorro, prefijo);

        db.Layouts.Add(new LayoutGenerado
        {
            Tipo = TipoLayout.Spei, CajaId = c.CajaId, CicloId = c.Id, Formato = ConversionesEnum.ADb(formato),
            NombreArchivo = archivo.NombreArchivo, Registros = archivo.Registros, ImporteTotal = archivo.ImporteTotal,
            SumaControl = archivo.SumaControl, UsuarioId = usuario.Id, Fecha = reloj.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);
        return archivo;
    }

    private sealed class FilaLayout
    {
        public int Id { get; set; }
        public string Tipo { get; set; } = "";
        public int CajaId { get; set; }
        public string Formato { get; set; } = "";
        public string NombreArchivo { get; set; } = "";
        public int Registros { get; set; }
        public decimal ImporteTotal { get; set; }
        public string? SumaControl { get; set; }
        public string Usuario { get; set; } = "";
        public DateTime Fecha { get; set; }
    }

    public async Task<Pagina<LayoutDto>> ListarLayoutsAsync(int id, ConsultaPaginada consulta, CancellationToken ct = default)
    {
        await CargarAsync(id, ct);
        var total = await db.EscalarAsync<int>(
            "SELECT CAST(COUNT(*) AS SIGNED) FROM layout_generado WHERE ciclo_id = @id", new { id }, ct);
        var filas = await db.ConsultarAsync<FilaLayout>(
            """
            SELECT l.id AS Id, l.tipo AS Tipo, l.caja_id AS CajaId, l.formato AS Formato, l.nombre_archivo AS NombreArchivo,
                   l.registros AS Registros, l.importe_total AS ImporteTotal, l.suma_control AS SumaControl,
                   u.nombre AS Usuario, l.fecha AS Fecha
              FROM layout_generado l JOIN usuario u ON u.id = l.usuario_id
             WHERE l.ciclo_id = @id
             ORDER BY l.fecha DESC, l.id DESC
             LIMIT @tomar OFFSET @saltar
            """, new { id, tomar = consulta.TamanoSeguro, saltar = consulta.Saltar }, ct);
        var items = filas.Select(f => new LayoutDto(f.Id, ConversionesEnum.DeDb<TipoLayout>(f.Tipo), f.CajaId, null, null,
            f.Formato, f.NombreArchivo, f.Registros, f.ImporteTotal, f.SumaControl, f.Usuario, f.Fecha)).ToList();
        return new Pagina<LayoutDto>(items, consulta.PaginaSegura, consulta.TamanoSeguro, total);
    }
}

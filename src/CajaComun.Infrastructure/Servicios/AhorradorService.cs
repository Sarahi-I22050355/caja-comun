using System.Text;
using CajaComun.Application.Ahorradores;
using CajaComun.Application.Comun;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;
using CajaComun.Infrastructure.Persistence;
using Dapper;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace CajaComun.Infrastructure.Servicios;

public sealed class AhorradorService(
    CajaComunDbContext db, IFabricaConexion conexiones, ICajaAcceso acceso, TimeProvider reloj, ICacheDatos? cache = null) : IAhorradorService
{
    private readonly ICacheDatos _cache = cache ?? new SinCache();
    private DateOnly Hoy => DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);

    // ---------- Listado ----------

    private sealed record FilaLista(
        int Id, int CajaId, string Caja, string NumEmpleado, string Nombre, string Apellidos, string Departamento,
        string Estado, decimal SueldoQuincenal, decimal AhorroTotal);

    private sealed record FilaSaldo(int AhorradorId, decimal Total);

    // Columnas por las que se puede ordenar: la lista blanca evita armar SQL con texto del cliente.
    private static string ColumnasOrden(string? orden, bool desc)
    {
        var dir = desc ? " DESC" : "";
        return (orden ?? "nombre").ToLowerInvariant() switch
        {
            "numempleado" => $"a.num_empleado{dir}, a.id",
            "ahorro" => $"AhorroTotal{dir}, a.id",
            "ingreso" => $"a.fecha_ingreso{dir}, a.id",
            _ => $"a.apellidos{dir}, a.nombre{dir}, a.id"
        };
    }

    public async Task<Pagina<AhorradorListaDto>> ListarAsync(ConsultaAhorradores consulta, CancellationToken ct = default)
    {
        var permitidas = await acceso.CajasPermitidasAsync(ct);
        IReadOnlyCollection<int> cajas = permitidas;
        if (consulta.CajaId is { } cajaId)
        {
            await acceso.ExigirAsync(cajaId, ct);
            cajas = [cajaId];
        }
        if (cajas.Count == 0) return new Pagina<AhorradorListaDto>([], consulta.PaginaSegura, consulta.TamanoSeguro, 0);

        var where = new StringBuilder("a.caja_id IN @cajas");
        var p = new DynamicParameters();
        p.Add("cajas", cajas);
        if (consulta.Estado is { } estado)
        {
            where.Append(" AND a.estado = @estado");
            p.Add("estado", ConversionesEnum.ADb(estado));
        }
        if (!string.IsNullOrWhiteSpace(consulta.Q))
        {
            where.Append(" AND (a.num_empleado LIKE @q OR a.nombre LIKE @q OR a.apellidos LIKE @q OR CONCAT(a.nombre, ' ', a.apellidos) LIKE @q)");
            p.Add("q", "%" + EscaparLike(consulta.Q.Trim()) + "%");
        }
        p.Add("tomar", consulta.TamanoSeguro);
        p.Add("saltar", consulta.Saltar);

        var ordenaPorAhorro = string.Equals(consulta.Orden, "ahorro", StringComparison.OrdinalIgnoreCase);
        var orden = ColumnasOrden(consulta.Orden, consulta.Desc);

        // Solo cuando se ordena por ahorro hace falta calcular el saldo de todo el padrón; en los demás
        // casos se pide el saldo únicamente de los renglones de la página.
        var sqlPagina = ordenaPorAhorro
            ? $"""
               SELECT a.id AS Id, a.caja_id AS CajaId, c.nombre AS Caja, a.num_empleado AS NumEmpleado, a.nombre AS Nombre,
                      a.apellidos AS Apellidos, a.departamento AS Departamento, a.estado AS Estado,
                      a.sueldo_quincenal AS SueldoQuincenal, COALESCE(s.total, 0) AS AhorroTotal
                 FROM ahorrador a
                 JOIN caja c ON c.id = a.caja_id
                 LEFT JOIN v_saldo_ahorrador s ON s.ahorrador_id = a.id
                WHERE {where}
                ORDER BY {orden}
                LIMIT @tomar OFFSET @saltar
               """
            : $"""
               SELECT a.id AS Id, a.caja_id AS CajaId, c.nombre AS Caja, a.num_empleado AS NumEmpleado, a.nombre AS Nombre,
                      a.apellidos AS Apellidos, a.departamento AS Departamento, a.estado AS Estado,
                      a.sueldo_quincenal AS SueldoQuincenal, CAST(0 AS DECIMAL(14,2)) AS AhorroTotal
                 FROM ahorrador a
                 JOIN caja c ON c.id = a.caja_id
                WHERE {where}
                ORDER BY {orden}
                LIMIT @tomar OFFSET @saltar
               """;

        await using var cn = await conexiones.AbrirAsync(ct);
        var total = await cn.ExecuteScalarAsync<int>(
            new CommandDefinition($"SELECT COUNT(*) FROM ahorrador a WHERE {where}", p, cancellationToken: ct));
        var filas = (await cn.QueryAsync<FilaLista>(new CommandDefinition(sqlPagina, p, cancellationToken: ct))).ToList();

        Dictionary<int, decimal> saldos = [];
        if (!ordenaPorAhorro && filas.Count > 0)
        {
            var ids = filas.Select(f => f.Id).ToArray();
            var s = await cn.QueryAsync<FilaSaldo>(new CommandDefinition(
                "SELECT ahorrador_id AS AhorradorId, total AS Total FROM v_saldo_ahorrador WHERE ahorrador_id IN @ids",
                new { ids }, cancellationToken: ct));
            saldos = s.ToDictionary(x => x.AhorradorId, x => x.Total);
        }

        var items = filas.Select(f => new AhorradorListaDto(
            f.Id, f.CajaId, f.Caja, f.NumEmpleado, f.Nombre, f.Apellidos, f.Departamento,
            ConversionesEnum.DeDb<EstadoAhorrador>(f.Estado), f.SueldoQuincenal,
            ordenaPorAhorro ? f.AhorroTotal : saldos.GetValueOrDefault(f.Id))).ToList();
        return new Pagina<AhorradorListaDto>(items, consulta.PaginaSegura, consulta.TamanoSeguro, total);
    }

    // ---------- Detalle, alta y edición ----------

    public async Task<AhorradorDetalleDto> ObtenerAsync(int id, CancellationToken ct = default)
    {
        await CargarAccesibleAsync(id, ct);
        return await ArmarDetalleAsync(id, ct);
    }

    public async Task<AhorradorDetalleDto> CrearAsync(CrearAhorradorRequest request, CancellationToken ct = default)
    {
        await acceso.ExigirAsync(request.CajaId, ct);
        var caja = await db.Cajas.AsNoTracking().FirstOrDefaultAsync(c => c.Id == request.CajaId, ct)
            ?? throw ErrorNegocio.NoEncontrado(Codigos.CajaNoEncontrada, "La caja no existe.");
        if (!caja.Activa)
            throw ErrorNegocio.Conflicto(Codigos.CajaInactiva, "La caja está inactiva y no admite altas.");

        var numero = string.IsNullOrWhiteSpace(request.NumEmpleado)
            ? await SiguienteNumeroAsync(caja, ct)
            : request.NumEmpleado.Trim();
        if (!numero.StartsWith(caja.Prefijo + "-", StringComparison.Ordinal))
            throw new ErrorNegocio(Codigos.Validacion, "El número de empleado debe llevar el prefijo de la caja.", 400,
                new Dictionary<string, string[]> { ["numEmpleado"] = ["NUM_EMPLEADO_PREFIJO"] });
        if (await db.Ahorradores.AnyAsync(a => a.CajaId == caja.Id && a.NumEmpleado == numero, ct))
            throw ErrorNegocio.Conflicto(Codigos.NumEmpleadoDuplicado, "Ese número de empleado ya existe en la caja.");

        // La primera cuota rige desde el ingreso o, si es anterior, desde el primer periodo abierto: en los periodos
        // ya aplicados no hubo descuentos y la vista de descuentos no debe inventarlos.
        var primerAbierto = await db.Periodos.AsNoTracking()
            .Where(p => p.CajaId == caja.Id && p.Estado == EstadoPeriodo.Abierto)
            .OrderBy(p => p.FechaInicio).Select(p => (DateOnly?)p.FechaInicio).FirstOrDefaultAsync(ct);
        var vigenteDesde = primerAbierto is { } inicio && inicio > request.FechaIngreso ? inicio : request.FechaIngreso;

        var ahorrador = new Ahorrador
        {
            CajaId = caja.Id,
            NumEmpleado = numero,
            Nombre = request.Nombre.Trim(),
            Apellidos = request.Apellidos.Trim(),
            Rfc = request.Rfc.Trim().ToUpperInvariant(),
            FechaNacimiento = request.FechaNacimiento,
            FechaIngreso = request.FechaIngreso,
            Estado = EstadoAhorrador.Activo,
            SueldoQuincenal = request.SueldoQuincenal,
            Clabe = request.Clabe,
            Departamento = request.Departamento.Trim()
        };
        db.Ahorradores.Add(ahorrador);
        db.CuotasAhorro.Add(new CuotaAhorro
        {
            Ahorrador = ahorrador,
            CuotaOrdinaria = request.CuotaOrdinaria,
            CuotaVoluntaria = request.VoluntarioActivo ? request.CuotaVoluntaria : 0m,
            VoluntarioActivo = request.VoluntarioActivo,
            VigenteDesde = vigenteDesde
        });

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
        {
            // Dos altas simultáneas pudieron calcular el mismo consecutivo: la llave única decide.
            throw ErrorNegocio.Conflicto(Codigos.NumEmpleadoDuplicado, "Ese número de empleado ya existe en la caja.");
        }
        await _cache.InvalidarAsync(caja.Id);
        return await ArmarDetalleAsync(ahorrador.Id, ct);
    }

    public async Task<AhorradorDetalleDto> ActualizarAsync(int id, ActualizarAhorradorRequest request, CancellationToken ct = default)
    {
        await CargarAccesibleAsync(id, ct);
        var a = await db.Ahorradores.FirstAsync(x => x.Id == id, ct);
        a.Nombre = request.Nombre.Trim();
        a.Apellidos = request.Apellidos.Trim();
        a.Rfc = request.Rfc.Trim().ToUpperInvariant();
        a.FechaNacimiento = request.FechaNacimiento;
        a.SueldoQuincenal = request.SueldoQuincenal;
        a.Clabe = request.Clabe;
        a.Departamento = request.Departamento.Trim();
        await db.SaveChangesAsync(ct);
        return await ArmarDetalleAsync(id, ct);
    }

    private sealed record FilaPrestamo(string Folio, decimal Monto, int PlazoQuincenas, decimal TasaMensual, decimal SaldoVigente, long CuotasRestantes);
    private sealed record FilaAhorro(decimal Ordinario, decimal Voluntario, decimal Total);

    private async Task<AhorradorDetalleDto> ArmarDetalleAsync(int id, CancellationToken ct)
    {
        var a = await db.Ahorradores.AsNoTracking().Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id, x.CajaId, Caja = x.Caja.Nombre, x.NumEmpleado, x.Nombre, x.Apellidos, x.Rfc, x.FechaNacimiento,
                x.FechaIngreso, x.FechaBaja, x.Estado, x.SueldoQuincenal, x.Clabe, x.Departamento
            }).FirstAsync(ct);

        var hoy = Hoy;
        var cuota = await db.CuotasAhorro.AsNoTracking()
            .Where(c => c.AhorradorId == id && c.VigenteDesde <= hoy)
            .OrderByDescending(c => c.VigenteDesde)
            .Select(c => new CuotaDto(c.Id, c.CuotaOrdinaria, c.CuotaVoluntaria, c.VoluntarioActivo, c.VigenteDesde))
            .FirstOrDefaultAsync(ct);

        await using var cn = await conexiones.AbrirAsync(ct);
        var ahorro = await cn.QuerySingleOrDefaultAsync<FilaAhorro>(new CommandDefinition(
            "SELECT ordinario AS Ordinario, voluntario AS Voluntario, total AS Total FROM v_saldo_ahorrador WHERE ahorrador_id = @id",
            new { id }, cancellationToken: ct)) ?? new FilaAhorro(0m, 0m, 0m);
        var prestamos = (await cn.QueryAsync<FilaPrestamo>(new CommandDefinition(
            """
            SELECT folio AS Folio, monto AS Monto, plazo_quincenas AS PlazoQuincenas, tasa_mensual AS TasaMensual,
                   saldo_vigente AS SaldoVigente, CAST(cuotas_restantes AS SIGNED) AS CuotasRestantes
              FROM v_prestamo_resumen
             WHERE ahorrador_id = @id AND estado = 'VIGENTE' AND saldo_vigente > 0
             ORDER BY folio
            """, new { id }, cancellationToken: ct))).ToList();

        var saldoPrestamos = prestamos.Sum(x => x.SaldoVigente);
        var resumen = new AhorroResumenDto(
            ahorro.Ordinario, ahorro.Voluntario, ahorro.Total,
            CapacidadCredito.Limite(ahorro.Total), saldoPrestamos,
            CapacidadCredito.DisponibleSinAval(ahorro.Total, saldoPrestamos));

        return new AhorradorDetalleDto(
            a.Id, a.CajaId, a.Caja, a.NumEmpleado, a.Nombre, a.Apellidos, a.Rfc, a.FechaNacimiento, a.FechaIngreso,
            a.FechaBaja, a.Estado, a.SueldoQuincenal, a.Clabe, a.Departamento, cuota, resumen,
            prestamos.Select(x => new PrestamoVigenteDto(x.Folio, x.Monto, x.PlazoQuincenas, x.TasaMensual, x.SaldoVigente, (int)x.CuotasRestantes)).ToList());
    }

    private async Task<string> SiguienteNumeroAsync(Caja caja, CancellationToken ct)
    {
        var mayor = await db.Database.SqlQuery<int>($"""
            SELECT CAST(COALESCE(MAX(CAST(SUBSTRING(num_empleado, {caja.Prefijo.Length + 2}) AS UNSIGNED)), 0) AS SIGNED) AS Value
              FROM ahorrador
             WHERE caja_id = {caja.Id} AND num_empleado LIKE CONCAT({caja.Prefijo}, '-%')
            """).SingleAsync(ct);
        return ReglasAhorrador.NumeroEmpleado(caja.Prefijo, mayor + 1);
    }

    // ---------- Movimientos y estado de cuenta ----------

    private sealed record FilaMovimiento(
        long Id, DateTime Fecha, string Tipo, decimal Monto, string? Referencia, int? PeriodoId, string? FolioPrestamo,
        decimal EfectoAhorro, decimal SaldoAhorro);

    private static MovimientoDto Mapear(FilaMovimiento f) => new(
        f.Id, DateOnly.FromDateTime(f.Fecha), ConversionesEnum.DeDb<TipoMovimiento>(f.Tipo), f.Monto, f.Referencia,
        f.PeriodoId, f.FolioPrestamo, f.EfectoAhorro, f.SaldoAhorro);

    public async Task<Pagina<MovimientoDto>> ListarMovimientosAsync(int id, ConsultaMovimientos consulta, CancellationToken ct = default)
    {
        await CargarAccesibleAsync(id, ct);

        var where = new StringBuilder("v.ahorrador_id = @id");
        var p = new DynamicParameters();
        p.Add("id", id);
        if (consulta.Tipo is { } tipo) { where.Append(" AND v.tipo = @tipo"); p.Add("tipo", ConversionesEnum.ADb(tipo)); }
        if (consulta.Desde is { } desde) { where.Append(" AND v.fecha >= @desde"); p.Add("desde", desde); }
        if (consulta.Hasta is { } hasta) { where.Append(" AND v.fecha <= @hasta"); p.Add("hasta", hasta); }
        if (consulta.PeriodoId is { } periodo) { where.Append(" AND v.periodo_id = @periodo"); p.Add("periodo", periodo); }
        p.Add("tomar", consulta.TamanoSeguro);
        p.Add("saltar", consulta.Saltar);

        // El saldo acumulado lo calcula la vista sobre todo el historial; los filtros solo recortan lo que se muestra.
        var sql = $"""
            SELECT v.movimiento_id AS Id, v.fecha AS Fecha, v.tipo AS Tipo, v.monto AS Monto, v.referencia AS Referencia,
                   v.periodo_id AS PeriodoId, pr.folio AS FolioPrestamo, v.efecto_ahorro AS EfectoAhorro, v.saldo_ahorro AS SaldoAhorro
              FROM v_estado_cuenta v
              LEFT JOIN prestamo pr ON pr.id = v.prestamo_id
             WHERE {where}
             ORDER BY v.fecha DESC, v.movimiento_id DESC
             LIMIT @tomar OFFSET @saltar
            """;

        await using var cn = await conexiones.AbrirAsync(ct);
        var total = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            $"SELECT COUNT(*) FROM movimiento v WHERE {where}", p, cancellationToken: ct));
        var filas = await cn.QueryAsync<FilaMovimiento>(new CommandDefinition(sql, p, cancellationToken: ct));
        return new Pagina<MovimientoDto>(filas.Select(Mapear).ToList(), consulta.PaginaSegura, consulta.TamanoSeguro, total);
    }

    public async Task<EstadoCuentaDto> EstadoCuentaAsync(int id, int anio, CancellationToken ct = default)
    {
        var a = await CargarAccesibleAsync(id, ct);
        var caja = await db.Cajas.AsNoTracking().Where(c => c.Id == a.CajaId).Select(c => c.Nombre).FirstAsync(ct);
        var inicio = new DateOnly(anio, 1, 1);
        var fin = new DateOnly(anio, 12, 31);

        await using var cn = await conexiones.AbrirAsync(ct);
        var saldoInicial = await cn.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "SELECT fn_saldo_ahorro(@id, @corte)", new { id, corte = inicio.AddDays(-1) }, cancellationToken: ct));
        var filas = (await cn.QueryAsync<FilaMovimiento>(new CommandDefinition(
            """
            SELECT v.movimiento_id AS Id, v.fecha AS Fecha, v.tipo AS Tipo, v.monto AS Monto, v.referencia AS Referencia,
                   v.periodo_id AS PeriodoId, pr.folio AS FolioPrestamo, v.efecto_ahorro AS EfectoAhorro, v.saldo_ahorro AS SaldoAhorro
              FROM v_estado_cuenta v
              LEFT JOIN prestamo pr ON pr.id = v.prestamo_id
             WHERE v.ahorrador_id = @id AND v.fecha BETWEEN @inicio AND @fin
             ORDER BY v.fecha, v.movimiento_id
            """, new { id, inicio, fin }, cancellationToken: ct))).Select(Mapear).ToList();

        decimal Suma(TipoMovimiento t) => filas.Where(m => m.Tipo == t).Sum(m => m.Monto);
        var totales = new EstadoCuentaTotalesDto(
            Suma(TipoMovimiento.AhorroOrdinario), Suma(TipoMovimiento.AhorroVoluntario), Suma(TipoMovimiento.LiquidacionCiclo),
            Suma(TipoMovimiento.DesembolsoPrestamo), Suma(TipoMovimiento.AbonoCapital), Suma(TipoMovimiento.AbonoInteres),
            Suma(TipoMovimiento.InteresAnual));
        var saldoFinal = saldoInicial + filas.Sum(m => m.EfectoAhorro);

        return new EstadoCuentaDto(a.Id, a.NumEmpleado, $"{a.Nombre} {a.Apellidos}", caja, anio, saldoInicial, saldoFinal, totales, filas);
    }

    // ---------- Cuotas ----------

    public async Task<IReadOnlyList<CuotaDto>> ListarCuotasAsync(int id, CancellationToken ct = default)
    {
        await CargarAccesibleAsync(id, ct);
        return await db.CuotasAhorro.AsNoTracking().Where(c => c.AhorradorId == id)
            .OrderByDescending(c => c.VigenteDesde)
            .Select(c => new CuotaDto(c.Id, c.CuotaOrdinaria, c.CuotaVoluntaria, c.VoluntarioActivo, c.VigenteDesde))
            .ToListAsync(ct);
    }

    public async Task<CuotaDto> CrearCuotaAsync(int id, CrearCuotaRequest request, CancellationToken ct = default)
    {
        var a = await CargarAccesibleAsync(id, ct);
        if (a.Estado == EstadoAhorrador.Baja)
            throw ErrorNegocio.Conflicto(Codigos.AhorradorEnBaja, "El ahorrador está dado de baja.");

        // Una cuota no puede cambiar descuentos de periodos que ya se aplicaron: rige desde el primer periodo abierto.
        var primerAbierto = await db.Periodos.AsNoTracking()
            .Where(p => p.CajaId == a.CajaId && p.Estado == EstadoPeriodo.Abierto)
            .OrderBy(p => p.FechaInicio)
            .Select(p => (DateOnly?)p.FechaInicio)
            .FirstOrDefaultAsync(ct);
        if (primerAbierto is null || request.VigenteDesde < primerAbierto || request.VigenteDesde < a.FechaIngreso)
            throw new ErrorNegocio(Codigos.CuotaFechaInvalida,
                "La cuota debe regir desde un periodo abierto y no antes del ingreso del ahorrador.", 400,
                new Dictionary<string, string[]> { ["vigenteDesde"] = [Codigos.CuotaFechaInvalida] });

        if (await db.CuotasAhorro.AnyAsync(c => c.AhorradorId == id && c.VigenteDesde == request.VigenteDesde, ct))
            throw ErrorNegocio.Conflicto(Codigos.CuotaFechaDuplicada, "Ya hay una cuota con esa fecha de inicio.");

        var cuota = new CuotaAhorro
        {
            AhorradorId = id,
            CuotaOrdinaria = request.CuotaOrdinaria,
            CuotaVoluntaria = request.VoluntarioActivo ? request.CuotaVoluntaria : 0m,
            VoluntarioActivo = request.VoluntarioActivo,
            VigenteDesde = request.VigenteDesde
        };
        db.CuotasAhorro.Add(cuota);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
        {
            throw ErrorNegocio.Conflicto(Codigos.CuotaFechaDuplicada, "Ya hay una cuota con esa fecha de inicio.");
        }
        return new CuotaDto(cuota.Id, cuota.CuotaOrdinaria, cuota.CuotaVoluntaria, cuota.VoluntarioActivo, cuota.VigenteDesde);
    }

    // ---------- Utilidades ----------

    /// <summary>Un ahorrador de una caja ajena responde 404, igual que uno inexistente: no se revela que existe.</summary>
    private async Task<Ahorrador> CargarAccesibleAsync(int id, CancellationToken ct)
    {
        var permitidas = await acceso.CajasPermitidasAsync(ct);
        var a = await db.Ahorradores.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && permitidas.Contains(x.CajaId), ct);
        return a ?? throw ErrorNegocio.NoEncontrado(Codigos.AhorradorNoEncontrado, "El ahorrador no existe.");
    }

    private static string EscaparLike(string texto) =>
        texto.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}

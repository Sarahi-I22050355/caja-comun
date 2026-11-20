using System.Text;
using CajaComun.Application.Comun;
using CajaComun.Application.Prestamos;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;
using CajaComun.Infrastructure.Persistence;
using Dapper;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace CajaComun.Infrastructure.Servicios;

public sealed class PrestamoService(
    CajaComunDbContext db, ICajaAcceso acceso, IUsuarioActual usuario, TimeProvider reloj, ICacheDatos? cache = null) : IPrestamoService
{
    private readonly ICacheDatos _cache = cache ?? new SinCache();
    private readonly CalendarioPeriodos _calendario = new(db);

    private DateOnly Hoy => DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);

    // ---------- Evaluación de la garantía ----------

    private sealed record FilaSaldo(int AhorradorId, decimal Total);

    private async Task<decimal> AhorroAsync(int ahorradorId, CancellationToken ct) =>
        (await db.ConsultarAsync<FilaSaldo>(
            "SELECT ahorrador_id AS AhorradorId, total AS Total FROM v_saldo_ahorrador WHERE ahorrador_id = @ahorradorId",
            new { ahorradorId }, ct)).FirstOrDefault()?.Total ?? 0m;

    private Task<decimal> SaldoPrestamosAsync(int ahorradorId, CancellationToken ct) =>
        db.EscalarAsync<decimal>(
            """
            SELECT CAST(COALESCE(SUM(saldo_vigente), 0) AS DECIMAL(14,2)) FROM v_prestamo_resumen
             WHERE ahorrador_id = @ahorradorId AND estado = 'VIGENTE'
            """, new { ahorradorId }, ct);

    /// <summary>Lo que el aval ya respalda en otros préstamos (pendientes o vigentes).</summary>
    private Task<decimal> ComprometidoComoAvalAsync(int avalId, int? excluirPrestamoId, CancellationToken ct) =>
        db.EscalarAsync<decimal>(
            """
            SELECT CAST(COALESCE(SUM(excedente_aval), 0) AS DECIMAL(14,2)) FROM prestamo
             WHERE aval_ahorrador_id = @avalId AND estado IN ('PENDIENTE', 'VIGENTE') AND id <> @excluir
            """, new { avalId, excluir = excluirPrestamoId ?? 0 }, ct);

    private async Task<EvaluacionPrestamoDto> EvaluarAsync(
        Ahorrador solicitante, decimal monto, int? avalId, int? excluirPrestamoId, CancellationToken ct)
    {
        var ahorro = await AhorroAsync(solicitante.Id, ct);
        var saldo = await SaldoPrestamosAsync(solicitante.Id, ct);
        var excedente = CapacidadCredito.ExcedenteAval(monto, ahorro, saldo);

        AvalDto? aval = null;
        decimal? disponibleAval = null;
        var avalInvalido = false;
        if (excedente > 0m && avalId is { } id)
        {
            // El aval debe ser otro ahorrador activo de la misma caja.
            var a = id == solicitante.Id
                ? null
                : await db.Ahorradores.AsNoTracking().FirstOrDefaultAsync(
                    x => x.Id == id && x.CajaId == solicitante.CajaId && x.Estado == EstadoAhorrador.Activo, ct);
            if (a is null) avalInvalido = true;
            else
            {
                disponibleAval = EvaluadorGarantia.DisponibleComoAval(
                    await AhorroAsync(a.Id, ct), await SaldoPrestamosAsync(a.Id, ct),
                    await ComprometidoComoAvalAsync(a.Id, excluirPrestamoId, ct));
                aval = new AvalDto(a.Id, a.NumEmpleado, $"{a.Nombre} {a.Apellidos}", disponibleAval.Value);
            }
        }

        var ev = EvaluadorGarantia.Evaluar(monto, ahorro, saldo, disponibleAval);
        return new EvaluacionPrestamoDto(
            ahorro, CapacidadCredito.Limite(ahorro), saldo, CapacidadCredito.DisponibleSinAval(ahorro, saldo),
            excedente > 0m, excedente, ev.Garantia, ev.Aprobada && !avalInvalido,
            avalInvalido ? Codigos.AvalInvalido : ev.Motivo, aval);
    }

    // ---------- Calendario de cuotas ----------

    private static List<CuotaPrestamoDto> Tabla(
        Caja caja, decimal monto, int plazo, decimal tasa, IReadOnlyList<Periodo>? periodos, EstadoAmortizacion? estado)
    {
        var cuotas = CalculadoraAmortizacion.Generar(monto, plazo, tasa, Periodicidad.PeriodosPorMes(caja.FrecuenciaNomina));
        return cuotas.Select(c =>
        {
            var p = periodos is { } l && c.Numero <= l.Count ? l[c.Numero - 1] : null;
            return new CuotaPrestamoDto(
                c.Numero, p is { Id: > 0 } ? p.Id : null,
                p is null ? null : Periodicidad.Referencia(caja.FrecuenciaNomina, p.Numero, p.Anio),
                p?.FechaFin, c.SaldoInicial, c.Capital, c.Interes, c.Total, c.SaldoFinal, estado);
        }).ToList();
    }

    private async Task<(Caja Caja, Periodo? Abierto)> CajaYPeriodoAsync(int cajaId, CancellationToken ct)
    {
        var caja = await db.Cajas.AsNoTracking().FirstAsync(c => c.Id == cajaId, ct);
        return (caja, await _calendario.PrimerAbiertoAsync(cajaId, ct));
    }

    private async Task<Ahorrador> CargarAhorradorAsync(int id, CancellationToken ct)
    {
        var permitidas = await acceso.CajasPermitidasAsync(ct);
        return await db.Ahorradores.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id && permitidas.Contains(a.CajaId), ct)
            ?? throw ErrorNegocio.NoEncontrado(Codigos.AhorradorNoEncontrado, "El ahorrador no existe.");
    }

    // ---------- Simulación y solicitud ----------

    public async Task<SimulacionPrestamoDto> SimularAsync(SolicitudPrestamoRequest request, CancellationToken ct = default)
    {
        var a = await CargarAhorradorAsync(request.AhorradorId, ct);
        var (caja, abierto) = await CajaYPeriodoAsync(a.CajaId, ct);
        var evaluacion = await EvaluarAsync(a, request.Monto, request.AvalAhorradorId, null, ct);

        // Sin crear nada: si el préstamo cruza el fin del ciclo, los periodos que faltan solo se proyectan.
        var periodos = abierto is null ? null : await _calendario.SiguientesAsync(caja, abierto, request.PlazoQuincenas, crear: false, ct);
        var tabla = Tabla(caja, request.Monto, request.PlazoQuincenas, caja.TasaInteresMensual, periodos, null);
        var intereses = tabla.Sum(c => c.Interes);
        return new SimulacionPrestamoDto(
            caja.Id, caja.FrecuenciaNomina, caja.TasaInteresMensual, request.PlazoQuincenas, evaluacion,
            tabla[0].Total, intereses, request.Monto + intereses, tabla);
    }

    public async Task<PrestamoDetalleDto> CrearAsync(SolicitudPrestamoRequest request, CancellationToken ct = default)
    {
        var a = await CargarAhorradorAsync(request.AhorradorId, ct);
        if (a.Estado == EstadoAhorrador.Baja)
            throw ErrorNegocio.Conflicto(Codigos.AhorradorEnBaja, "El ahorrador está dado de baja.");
        if (a.Estado != EstadoAhorrador.Activo)
            throw ErrorNegocio.Conflicto(Codigos.AhorradorNoActivo, "El ahorrador debe estar activo para solicitar un préstamo.");

        var (caja, abierto) = await CajaYPeriodoAsync(a.CajaId, ct);
        if (!caja.Activa) throw ErrorNegocio.Conflicto(Codigos.CajaInactiva, "La caja está inactiva.");
        if (abierto is null) throw ErrorNegocio.Conflicto(Codigos.SinPeriodoAbierto, "La caja no tiene un periodo abierto.");

        var solicitud = ReglasPrestamo.FechaOperacion(Hoy, abierto.FechaInicio, abierto.FechaFin);

        // El consecutivo del folio es global por año. Si dos solicitudes toman el mismo, la llave única decide y la
        // segunda reinicia toda la transacción: dentro de una misma transacción MariaDB repetiría la misma lectura
        // del máximo y volvería a chocar.
        for (var intento = 1; ; intento++)
        {
            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            try
            {
                // Se bloquean las filas del solicitante y del aval: de dos solicitudes simultáneas la segunda espera y
                // ve lo que la primera ya comprometió (una sola pendiente por ahorrador y el ahorro del aval no se usa
                // dos veces).
                await BloquearAhorradoresAsync([a.Id, request.AvalAhorradorId], ct);
                if (await db.Prestamos.AnyAsync(p => p.AhorradorId == a.Id && p.Estado == EstadoPrestamo.Pendiente, ct))
                    throw ErrorNegocio.Conflicto(Codigos.PrestamoPendienteExistente, "El ahorrador ya tiene una solicitud pendiente.");

                var evaluacion = await EvaluarAsync(a, request.Monto, request.AvalAhorradorId, null, ct);
                if (!evaluacion.Aprobada)
                    throw ErrorNegocio.Conflicto(evaluacion.Motivo ?? Codigos.AvalRequerido, "El monto excede lo que respalda la garantía.");

                var periodos = await _calendario.SiguientesAsync(caja, abierto, request.PlazoQuincenas, crear: true, ct);
                var prestamo = new Prestamo
                {
                    Folio = await SiguienteFolioAsync(solicitud.Year, ct), CajaId = caja.Id, AhorradorId = a.Id,
                    Monto = request.Monto, PlazoQuincenas = request.PlazoQuincenas, TasaMensual = caja.TasaInteresMensual,
                    Garantia = evaluacion.Garantia, ExcedenteAval = evaluacion.ExcedenteAval,
                    AvalAhorradorId = evaluacion.Garantia == GarantiaPrestamo.PagareAval ? request.AvalAhorradorId : null,
                    Estado = EstadoPrestamo.Pendiente, FechaSolicitud = solicitud
                };
                AgregarCuotas(prestamo, caja, periodos, EstadoAmortizacion.Proyectado);
                db.Prestamos.Add(prestamo);
                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
                await _cache.InvalidarAsync(caja.Id);
                return await ObtenerAsync(prestamo.Id, ct);
            }
            catch (DbUpdateException ex) when (intento < 10 && ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
            {
                // Se descarta la transacción (al salir del bloque) y se vuelve a intentar con lecturas nuevas.
            }
        }
    }

    /// <summary>Bloquea las filas de los ahorradores en orden de id (así dos transacciones no se bloquean entre sí).</summary>
    private Task BloquearAhorradoresAsync(IEnumerable<int?> ids, CancellationToken ct)
    {
        var lista = ids.Where(i => i is not null).Select(i => i!.Value).Distinct().Order().ToArray();
        return db.ConsultarAsync<int>("SELECT id FROM ahorrador WHERE id IN @lista ORDER BY id FOR UPDATE", new { lista }, ct);
    }

    private static void AgregarCuotas(Prestamo p, Caja caja, IReadOnlyList<Periodo> periodos, EstadoAmortizacion estado)
    {
        var cuotas = CalculadoraAmortizacion.Generar(p.Monto, p.PlazoQuincenas, p.TasaMensual, Periodicidad.PeriodosPorMes(caja.FrecuenciaNomina));
        foreach (var c in cuotas)
            p.Amortizacion.Add(new PrestamoAmortizacion
            {
                Numero = c.Numero, PeriodoId = periodos[c.Numero - 1].Id, Fecha = periodos[c.Numero - 1].FechaFin,
                Capital = c.Capital, Interes = c.Interes, Total = c.Total, SaldoDespues = c.SaldoFinal, Estado = estado
            });
    }

    private async Task<string> SiguienteFolioAsync(int anio, CancellationToken ct)
    {
        var prefijo = $"FOL-{anio}-";
        var mayor = await db.EscalarAsync<int>(
            "SELECT CAST(COALESCE(MAX(CAST(SUBSTRING(folio, 10) AS UNSIGNED)), 0) AS SIGNED) FROM prestamo WHERE folio LIKE @patron",
            new { patron = prefijo + "%" }, ct);
        return ReglasPrestamo.Folio(anio, mayor + 1);
    }

    // ---------- Autorizar y rechazar ----------

    public async Task<PrestamoDetalleDto> AutorizarAsync(int id, CancellationToken ct = default)
    {
        var permitidas = await acceso.CajasPermitidasAsync(ct);
        var prestamo = await db.Prestamos.Include(p => p.Amortizacion)
            .FirstOrDefaultAsync(p => p.Id == id && permitidas.Contains(p.CajaId), ct)
            ?? throw ErrorNegocio.NoEncontrado(Codigos.PrestamoNoEncontrado, "El préstamo no existe.");
        if (prestamo.Estado != EstadoPrestamo.Pendiente)
            throw ErrorNegocio.Conflicto(Codigos.PrestamoNoPendiente, "El préstamo ya no está pendiente.");

        var a = await db.Ahorradores.AsNoTracking().FirstAsync(x => x.Id == prestamo.AhorradorId, ct);
        if (a.Estado != EstadoAhorrador.Activo)
            throw ErrorNegocio.Conflicto(a.Estado == EstadoAhorrador.Baja ? Codigos.AhorradorEnBaja : Codigos.AhorradorNoActivo,
                "El ahorrador ya no está activo.");
        var (caja, abierto) = await CajaYPeriodoAsync(prestamo.CajaId, ct);
        if (!caja.Activa) throw ErrorNegocio.Conflicto(Codigos.CajaInactiva, "La caja está inactiva.");
        if (abierto is null) throw ErrorNegocio.Conflicto(Codigos.SinPeriodoAbierto, "La caja no tiene un periodo abierto.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var cambiados = await db.EjecutarAsync(
            "UPDATE prestamo SET estado = 'VIGENTE' WHERE id = @id AND estado = 'PENDIENTE'", new { id }, ct);
        if (cambiados == 0)
            throw ErrorNegocio.Conflicto(Codigos.PrestamoNoPendiente, "El préstamo ya no está pendiente.");

        // Entre la solicitud y la autorización pudo cambiar el ahorro o lo que el aval ya respalda.
        await BloquearAhorradoresAsync([a.Id, prestamo.AvalAhorradorId], ct);
        var evaluacion = await EvaluarAsync(a, prestamo.Monto, prestamo.AvalAhorradorId, prestamo.Id, ct);
        if (!evaluacion.Aprobada)
            throw ErrorNegocio.Conflicto(evaluacion.Motivo ?? Codigos.AvalRequerido, "La garantía ya no cubre el monto.");

        prestamo.Estado = EstadoPrestamo.Vigente;
        prestamo.Garantia = evaluacion.Garantia;
        prestamo.ExcedenteAval = evaluacion.ExcedenteAval;
        if (evaluacion.Garantia == GarantiaPrestamo.Ahorro80) prestamo.AvalAhorradorId = null;
        prestamo.FechaAutorizacion = ReglasPrestamo.FechaOperacion(Hoy, abierto.FechaInicio, abierto.FechaFin);
        prestamo.AutorizadoPor = usuario.Id;

        // La tabla proyectada se rehace desde el periodo abierto actual: el desembolso es en la fecha de pago de
        // ese periodo y la primera cuota cae en el siguiente.
        db.Amortizaciones.RemoveRange(prestamo.Amortizacion);
        await db.SaveChangesAsync(ct);
        prestamo.Amortizacion.Clear();

        var periodos = await _calendario.SiguientesAsync(caja, abierto, prestamo.PlazoQuincenas, crear: true, ct);
        AgregarCuotas(prestamo, caja, periodos, EstadoAmortizacion.Programado);
        db.Movimientos.Add(new Movimiento
        {
            CajaId = caja.Id, AhorradorId = prestamo.AhorradorId, PrestamoId = prestamo.Id, Fecha = abierto.FechaFin,
            Tipo = TipoMovimiento.DesembolsoPrestamo, Monto = prestamo.Monto, Referencia = prestamo.Folio
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await _cache.InvalidarAsync(caja.Id);
        return await ObtenerAsync(id, ct);
    }

    public async Task<PrestamoDetalleDto> RechazarAsync(int id, CancellationToken ct = default)
    {
        var permitidas = await acceso.CajasPermitidasAsync(ct);
        var prestamo = await db.Prestamos
            .FirstOrDefaultAsync(p => p.Id == id && permitidas.Contains(p.CajaId), ct)
            ?? throw ErrorNegocio.NoEncontrado(Codigos.PrestamoNoEncontrado, "El préstamo no existe.");
        if (prestamo.Estado != EstadoPrestamo.Pendiente)
            throw ErrorNegocio.Conflicto(Codigos.PrestamoNoPendiente, "El préstamo ya no está pendiente.");

        var (_, abierto) = await CajaYPeriodoAsync(prestamo.CajaId, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var cambiados = await db.EjecutarAsync(
            "UPDATE prestamo SET estado = 'RECHAZADO' WHERE id = @id AND estado = 'PENDIENTE'", new { id }, ct);
        if (cambiados == 0)
            throw ErrorNegocio.Conflicto(Codigos.PrestamoNoPendiente, "El préstamo ya no está pendiente.");

        // En un préstamo rechazado la fecha y el usuario de autorización registran quién y cuándo resolvió.
        prestamo.Estado = EstadoPrestamo.Rechazado;
        prestamo.FechaAutorizacion = abierto is null ? Hoy : ReglasPrestamo.FechaOperacion(Hoy, abierto.FechaInicio, abierto.FechaFin);
        prestamo.AutorizadoPor = usuario.Id;
        await db.Amortizaciones.Where(a => a.PrestamoId == id).ExecuteDeleteAsync(ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await _cache.InvalidarAsync(prestamo.CajaId);
        return await ObtenerAsync(id, ct);
    }

    // ---------- Consultas ----------

    private sealed record FilaPrestamo(
        int Id, string Folio, int CajaId, string Caja, int AhorradorId, string NumEmpleado, string Ahorrador, decimal Monto,
        int PlazoQuincenas, decimal TasaMensual, string Garantia, int? AvalId, string? AvalNumero, string? AvalNombre,
        decimal ExcedenteAval, string Estado, DateTime FechaSolicitud, DateTime? FechaAutorizacion, string? AutorizadoPor,
        decimal CapitalPagado, decimal InteresPagado, decimal SaldoVigente, long CuotasAplicadas, long CuotasRestantes);

    private const string SelectPrestamo = """
        SELECT p.id AS Id, p.folio AS Folio, p.caja_id AS CajaId, c.nombre AS Caja, p.ahorrador_id AS AhorradorId,
               a.num_empleado AS NumEmpleado, CONCAT(a.nombre, ' ', a.apellidos) AS Ahorrador, p.monto AS Monto,
               p.plazo_quincenas AS PlazoQuincenas, p.tasa_mensual AS TasaMensual, p.garantia AS Garantia,
               p.aval_ahorrador_id AS AvalId, av.num_empleado AS AvalNumero, CONCAT(av.nombre, ' ', av.apellidos) AS AvalNombre,
               p.excedente_aval AS ExcedenteAval, p.estado AS Estado, p.fecha_solicitud AS FechaSolicitud,
               p.fecha_autorizacion AS FechaAutorizacion, u.nombre AS AutorizadoPor,
               r.capital_pagado AS CapitalPagado, r.interes_pagado AS InteresPagado, r.saldo_vigente AS SaldoVigente,
               CAST(r.cuotas_aplicadas AS SIGNED) AS CuotasAplicadas, CAST(r.cuotas_restantes AS SIGNED) AS CuotasRestantes
          FROM prestamo p
          JOIN caja c ON c.id = p.caja_id
          JOIN ahorrador a ON a.id = p.ahorrador_id
          JOIN v_prestamo_resumen r ON r.prestamo_id = p.id
          LEFT JOIN ahorrador av ON av.id = p.aval_ahorrador_id
          LEFT JOIN usuario u ON u.id = p.autorizado_por
        """;

    private static PrestamoDto Mapear(FilaPrestamo f) => new(
        f.Id, f.Folio, f.CajaId, f.Caja, f.AhorradorId, f.NumEmpleado, f.Ahorrador, f.Monto, f.PlazoQuincenas, f.TasaMensual,
        ConversionesEnum.DeDb<GarantiaPrestamo>(f.Garantia),
        f.AvalId is { } av ? new AvalResumenDto(av, f.AvalNumero ?? "", f.AvalNombre ?? "") : null,
        f.ExcedenteAval, ConversionesEnum.DeDb<EstadoPrestamo>(f.Estado), DateOnly.FromDateTime(f.FechaSolicitud),
        f.FechaAutorizacion is { } fa ? DateOnly.FromDateTime(fa) : null, f.AutorizadoPor, f.CapitalPagado, f.InteresPagado,
        f.SaldoVigente, (int)f.CuotasAplicadas, (int)f.CuotasRestantes);

    private static string Orden(string? orden, bool desc)
    {
        var dir = desc ? " DESC" : "";
        return (orden ?? "folio").ToLowerInvariant() switch
        {
            "monto" => $"p.monto{dir}, p.id",
            "saldo" => $"r.saldo_vigente{dir}, p.id",
            "solicitud" => $"p.fecha_solicitud{dir}, p.id",
            _ => $"p.folio{dir}, p.id"
        };
    }

    public async Task<Pagina<PrestamoDto>> ListarAsync(ConsultaPrestamos consulta, CancellationToken ct = default)
    {
        IReadOnlyCollection<int> cajas = await acceso.CajasPermitidasAsync(ct);
        if (consulta.CajaId is { } cajaId)
        {
            await acceso.ExigirAsync(cajaId, ct);
            cajas = [cajaId];
        }
        if (cajas.Count == 0) return new Pagina<PrestamoDto>([], consulta.PaginaSegura, consulta.TamanoSeguro, 0);

        var where = new StringBuilder("p.caja_id IN @cajas");
        var par = new DynamicParameters();
        par.Add("cajas", cajas);
        if (consulta.AhorradorId is { } ahorrador) { where.Append(" AND p.ahorrador_id = @ahorrador"); par.Add("ahorrador", ahorrador); }
        if (consulta.Estado is { } estado) { where.Append(" AND p.estado = @estado"); par.Add("estado", ConversionesEnum.ADb(estado)); }
        if (!string.IsNullOrWhiteSpace(consulta.Q))
        {
            where.Append(" AND (p.folio LIKE @q OR a.num_empleado LIKE @q OR a.nombre LIKE @q OR a.apellidos LIKE @q OR CONCAT(a.nombre, ' ', a.apellidos) LIKE @q)");
            par.Add("q", "%" + consulta.Q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
        }
        par.Add("tomar", consulta.TamanoSeguro);
        par.Add("saltar", consulta.Saltar);

        var total = await db.EscalarAsync<int>(
            $"SELECT CAST(COUNT(*) AS SIGNED) FROM prestamo p JOIN ahorrador a ON a.id = p.ahorrador_id WHERE {where}", par, ct);
        var filas = await db.ConsultarAsync<FilaPrestamo>(
            $"{SelectPrestamo} WHERE {where} ORDER BY {Orden(consulta.Orden, consulta.Desc)} LIMIT @tomar OFFSET @saltar", par, ct);
        return new Pagina<PrestamoDto>(filas.Select(Mapear).ToList(), consulta.PaginaSegura, consulta.TamanoSeguro, total);
    }

    private sealed record FilaResumen(
        long Vigentes, long Pendientes, long Liquidados, long Rechazados, decimal CapitalColocado, decimal SaldoVigente,
        decimal InteresCobrado);

    public async Task<CarteraResumenDto> ResumenAsync(int? cajaId, CancellationToken ct = default)
    {
        IReadOnlyCollection<int> cajas = await acceso.CajasPermitidasAsync(ct);
        if (cajaId is { } id)
        {
            await acceso.ExigirAsync(id, ct);
            cajas = [id];
        }
        if (cajas.Count == 0) return new CarteraResumenDto(0, 0, 0, 0, 0m, 0m, 0m);

        var f = (await db.ConsultarAsync<FilaResumen>(
            """
            SELECT CAST(COALESCE(SUM(estado = 'VIGENTE'), 0) AS SIGNED) AS Vigentes,
                   CAST(COALESCE(SUM(estado = 'PENDIENTE'), 0) AS SIGNED) AS Pendientes,
                   CAST(COALESCE(SUM(estado = 'LIQUIDADO'), 0) AS SIGNED) AS Liquidados,
                   CAST(COALESCE(SUM(estado = 'RECHAZADO'), 0) AS SIGNED) AS Rechazados,
                   CAST(COALESCE(SUM(CASE WHEN estado IN ('VIGENTE', 'LIQUIDADO') THEN monto ELSE 0 END), 0) AS DECIMAL(14,2)) AS CapitalColocado,
                   CAST(COALESCE(SUM(CASE WHEN estado = 'VIGENTE' THEN saldo_vigente ELSE 0 END), 0) AS DECIMAL(14,2)) AS SaldoVigente,
                   CAST(COALESCE(SUM(interes_pagado), 0) AS DECIMAL(14,2)) AS InteresCobrado
              FROM v_prestamo_resumen WHERE caja_id IN @cajas
            """, new { cajas }, ct)).Single();
        return new CarteraResumenDto((int)f.Vigentes, (int)f.Pendientes, (int)f.Liquidados, (int)f.Rechazados,
            f.CapitalColocado, f.SaldoVigente, f.InteresCobrado);
    }

    private sealed record FilaCuota(
        int Numero, int PeriodoId, string Periodo, DateTime Fecha, decimal Capital, decimal Interes, decimal Total,
        decimal SaldoDespues, string Estado);

    public async Task<PrestamoDetalleDto> ObtenerAsync(int id, CancellationToken ct = default)
    {
        var permitidas = await acceso.CajasPermitidasAsync(ct);
        var fila = (await db.ConsultarAsync<FilaPrestamo>(
            $"{SelectPrestamo} WHERE p.id = @id AND p.caja_id IN @permitidas", new { id, permitidas }, ct)).FirstOrDefault()
            ?? throw ErrorNegocio.NoEncontrado(Codigos.PrestamoNoEncontrado, "El préstamo no existe.");

        var cuotas = (await db.ConsultarAsync<FilaCuota>(
            """
            SELECT am.numero AS Numero, am.periodo_id AS PeriodoId,
                   CONCAT(IF(c.frecuencia_nomina = 'QUINCENAL', 'Q', 'S'), pe.numero, '-', pe.anio) AS Periodo,
                   am.fecha AS Fecha, am.capital AS Capital, am.interes AS Interes, am.total AS Total,
                   am.saldo_despues AS SaldoDespues, am.estado AS Estado
              FROM prestamo_amortizacion am
              JOIN periodo pe ON pe.id = am.periodo_id
              JOIN caja c ON c.id = pe.caja_id
             WHERE am.prestamo_id = @id
             ORDER BY am.numero
            """, new { id }, ct))
            .Select(f => new CuotaPrestamoDto(
                f.Numero, f.PeriodoId, f.Periodo, DateOnly.FromDateTime(f.Fecha), f.SaldoDespues + f.Capital, f.Capital,
                f.Interes, f.Total, f.SaldoDespues, ConversionesEnum.DeDb<EstadoAmortizacion>(f.Estado)))
            .ToList();

        // La cuota "actual" es la próxima por cobrar de un préstamo vigente (la que la interfaz resalta).
        int? actual = fila.Estado == "VIGENTE"
            ? cuotas.FirstOrDefault(c => c.Estado == EstadoAmortizacion.Programado)?.Numero
            : null;
        return new PrestamoDetalleDto(Mapear(fila), cuotas, actual);
    }
}

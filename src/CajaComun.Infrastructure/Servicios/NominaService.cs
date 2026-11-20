using System.Text;
using CajaComun.Application.Cajas;
using CajaComun.Application.Comun;
using CajaComun.Application.Nomina;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;
using CajaComun.Infrastructure.Archivos;
using CajaComun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace CajaComun.Infrastructure.Servicios;

public sealed class NominaService(
    CajaComunDbContext db, ICajaAcceso acceso, IUsuarioActual usuario, TimeProvider reloj, ICacheDatos? cache = null) : INominaService
{
    private readonly ICacheDatos _cache = cache ?? new SinCache();
    private readonly CalendarioPeriodos _calendario = new(db);
    private readonly BajaAhorrador _baja = new(db);

    /// <summary>Un periodo de una caja ajena responde 404, igual que uno inexistente.</summary>
    private async Task<(Periodo Periodo, Caja Caja)> CargarPeriodoAsync(int periodoId, CancellationToken ct)
    {
        var permitidas = await acceso.CajasPermitidasAsync(ct);
        var p = await db.Periodos.AsNoTracking().FirstOrDefaultAsync(x => x.Id == periodoId && permitidas.Contains(x.CajaId), ct)
            ?? throw ErrorNegocio.NoEncontrado(Codigos.PeriodoNoEncontrado, "El periodo no existe.");
        var caja = await db.Cajas.AsNoTracking().FirstAsync(c => c.Id == p.CajaId, ct);
        return (p, caja);
    }

    // ---------- Descuentos del periodo ----------

    // Clase y no registro: el indicador de suspensión llega de la vista con un entero de ancho variable.
    private sealed class FilaDescuento
    {
        public int AhorradorId { get; set; }
        public string NumEmpleado { get; set; } = "";
        public string Nombre { get; set; } = "";
        public decimal Ordinaria { get; set; }
        public decimal Voluntaria { get; set; }
        public decimal Prestamo { get; set; }
        public decimal Total { get; set; }
        public long Suspendido { get; set; }
    }

    private sealed record FilaIncidencia(int AhorradorId, string Tipo);

    private sealed record FilaResumen(
        long Ahorradores, long Suspendidos, decimal Ordinaria, decimal Voluntaria, decimal Prestamo, decimal Total);

    public async Task<DescuentosPeriodoDto> DescuentosAsync(int periodoId, ConsultaDescuentos consulta, CancellationToken ct = default)
    {
        var (periodo, caja) = await CargarPeriodoAsync(periodoId, ct);

        var where = new StringBuilder("d.periodo_id = @periodo");
        var p = new Dapper.DynamicParameters();
        p.Add("periodo", periodoId);
        if (!string.IsNullOrWhiteSpace(consulta.Q))
        {
            where.Append(" AND (a.num_empleado LIKE @q OR a.nombre LIKE @q OR a.apellidos LIKE @q OR CONCAT(a.nombre, ' ', a.apellidos) LIKE @q)");
            p.Add("q", "%" + consulta.Q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
        }
        if (consulta.Suspendido is { } s)
        {
            where.Append(" AND d.suspendido = @suspendido");
            p.Add("suspendido", s ? 1 : 0);
        }
        p.Add("tomar", consulta.TamanoSeguro);
        p.Add("saltar", consulta.Saltar);

        var resumen = (await db.ConsultarAsync<FilaResumen>(
            """
            SELECT CAST(COUNT(*) AS SIGNED) AS Ahorradores, CAST(COALESCE(SUM(d.suspendido), 0) AS SIGNED) AS Suspendidos,
                   CAST(COALESCE(SUM(d.cuota_ordinaria), 0) AS DECIMAL(14,2)) AS Ordinaria,
                   CAST(COALESCE(SUM(d.cuota_voluntaria), 0) AS DECIMAL(14,2)) AS Voluntaria,
                   CAST(COALESCE(SUM(d.cuota_prestamo), 0) AS DECIMAL(14,2)) AS Prestamo,
                   CAST(COALESCE(SUM(d.total), 0) AS DECIMAL(14,2)) AS Total
              FROM v_descuento_periodo d
             WHERE d.periodo_id = @periodo
            """, new { periodo = periodoId }, ct)).Single();

        var total = await db.EscalarAsync<int>(
            $"SELECT CAST(COUNT(*) AS SIGNED) FROM v_descuento_periodo d JOIN ahorrador a ON a.id = d.ahorrador_id WHERE {where}", p, ct);
        var filas = await db.ConsultarAsync<FilaDescuento>(
            $"""
            SELECT a.id AS AhorradorId, a.num_empleado AS NumEmpleado, CONCAT(a.nombre, ' ', a.apellidos) AS Nombre,
                   d.cuota_ordinaria AS Ordinaria, d.cuota_voluntaria AS Voluntaria, d.cuota_prestamo AS Prestamo,
                   d.total AS Total, CAST(d.suspendido AS SIGNED) AS Suspendido
              FROM v_descuento_periodo d
              JOIN ahorrador a ON a.id = d.ahorrador_id
             WHERE {where}
             ORDER BY a.apellidos, a.nombre, a.id
             LIMIT @tomar OFFSET @saltar
            """, p, ct);

        var porAhorrador = new Dictionary<int, List<TipoIncidencia>>();
        if (filas.Count > 0)
        {
            var ids = filas.Select(f => f.AhorradorId).ToArray();
            var incidencias = await db.ConsultarAsync<FilaIncidencia>(
                "SELECT ahorrador_id AS AhorradorId, tipo AS Tipo FROM incidencia WHERE periodo_id = @periodo AND ahorrador_id IN @ids",
                new { periodo = periodoId, ids }, ct);
            foreach (var i in incidencias)
            {
                if (!porAhorrador.TryGetValue(i.AhorradorId, out var lista)) porAhorrador[i.AhorradorId] = lista = [];
                lista.Add(ConversionesEnum.DeDb<TipoIncidencia>(i.Tipo));
            }
        }

        var items = filas.Select(f => new DescuentoDto(
            f.AhorradorId, f.NumEmpleado, f.Nombre, f.Ordinaria, f.Voluntaria, f.Prestamo, f.Total, f.Suspendido != 0,
            porAhorrador.GetValueOrDefault(f.AhorradorId) ?? [])).ToList();

        return new DescuentosPeriodoDto(
            new PeriodoDto(periodo.Id, periodo.Anio, periodo.Numero, periodo.FechaInicio, periodo.FechaFin, periodo.Estado),
            caja.Id, caja.Nombre, caja.FrecuenciaNomina, Periodicidad.Referencia(caja.FrecuenciaNomina, periodo.Numero, periodo.Anio),
            new ResumenDescuentosDto((int)resumen.Ahorradores, (int)resumen.Suspendidos, resumen.Ordinaria, resumen.Voluntaria,
                resumen.Prestamo, resumen.Total),
            new Pagina<DescuentoDto>(items, consulta.PaginaSegura, consulta.TamanoSeguro, total));
    }

    // ---------- Incidencias ----------

    public async Task<IReadOnlyList<IncidenciaDto>> ListarIncidenciasAsync(int periodoId, CancellationToken ct = default)
    {
        await CargarPeriodoAsync(periodoId, ct);
        return await db.Incidencias.AsNoTracking()
            .Where(i => i.PeriodoId == periodoId)
            .OrderBy(i => i.Ahorrador.NumEmpleado).ThenBy(i => i.Tipo)
            .Select(i => new IncidenciaDto(i.Id, i.AhorradorId, i.Ahorrador.NumEmpleado, i.Ahorrador.Nombre + " " + i.Ahorrador.Apellidos,
                i.PeriodoId, i.Tipo, i.Valor, i.Nota))
            .ToListAsync(ct);
    }

    public async Task<IncidenciaDto> CrearIncidenciaAsync(CrearIncidenciaRequest request, CancellationToken ct = default)
    {
        var (periodo, caja) = await CargarPeriodoAsync(request.PeriodoId, ct);
        if (periodo.Estado != EstadoPeriodo.Abierto)
            throw ErrorNegocio.Conflicto(Codigos.PeriodoNoAbierto, "Solo se registran incidencias en periodos abiertos.");

        var ahorrador = await db.Ahorradores.FirstOrDefaultAsync(a => a.Id == request.AhorradorId && a.CajaId == caja.Id, ct)
            ?? throw ErrorNegocio.NoEncontrado(Codigos.AhorradorNoEncontrado, "El ahorrador no existe en la caja del periodo.");
        if (ahorrador.Estado == EstadoAhorrador.Baja)
            throw ErrorNegocio.Conflicto(Codigos.AhorradorEnBaja, "El ahorrador está dado de baja.");
        if (await db.Incidencias.AnyAsync(i => i.AhorradorId == ahorrador.Id && i.PeriodoId == periodo.Id && i.Tipo == request.Tipo, ct))
            throw ErrorNegocio.Conflicto(Codigos.IncidenciaDuplicada, "Ya hay una incidencia de ese tipo para el ahorrador en el periodo.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var incidencia = new Incidencia
        {
            AhorradorId = ahorrador.Id, PeriodoId = periodo.Id, Tipo = request.Tipo,
            Valor = request.Tipo == TipoIncidencia.CambioCuota ? request.Valor : null,
            Nota = string.IsNullOrWhiteSpace(request.Nota) ? null : request.Nota.Trim()
        };
        db.Incidencias.Add(incidencia);

        switch (request.Tipo)
        {
            case TipoIncidencia.Baja:
                await RegistrarBajaAsync(ahorrador, request.FechaBaja!.Value, periodo, ct);
                break;
            case TipoIncidencia.CambioCuota:
                await RegistrarCambioCuotaAsync(ahorrador, periodo, request.Valor!.Value, ct);
                break;
        }

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
        {
            throw ErrorNegocio.Conflicto(
                request.Tipo == TipoIncidencia.CambioCuota ? Codigos.CuotaFechaDuplicada : Codigos.IncidenciaDuplicada,
                "El registro ya existe.");
        }
        await tx.CommitAsync(ct);
        await _cache.InvalidarAsync(caja.Id);

        return new IncidenciaDto(incidencia.Id, ahorrador.Id, ahorrador.NumEmpleado, $"{ahorrador.Nombre} {ahorrador.Apellidos}",
            periodo.Id, incidencia.Tipo, incidencia.Valor, incidencia.Nota);
    }

    private async Task RegistrarBajaAsync(Ahorrador ahorrador, DateOnly fecha, Periodo periodo, CancellationToken ct)
    {
        var primero = await _calendario.PrimerAbiertoAsync(periodo.CajaId, ct);
        if (primero?.Id != periodo.Id)
            throw ErrorNegocio.Conflicto(Codigos.PeriodoNoSiguiente, "La baja se registra en el primer periodo abierto de la caja.");
        if (BajaAhorrador.ValidarFecha(ahorrador, fecha, periodo) is { } codigo)
            throw new ErrorNegocio(codigo, "La fecha de baja debe caer dentro del periodo y no ser anterior al ingreso.", 400,
                new Dictionary<string, string[]> { ["fechaBaja"] = [codigo] });
        var bloqueos = await _baja.BloqueosAsync([ahorrador.Id], ct);
        if (bloqueos.TryGetValue(ahorrador.Id, out var bloqueo))
            throw ErrorNegocio.Conflicto(bloqueo, "El ahorrador tiene un préstamo propio sin liquidar o avala uno.");

        var saldos = await _baja.SaldosAsync([ahorrador.Id], ct);
        _baja.Ejecutar(ahorrador, fecha, saldos.GetValueOrDefault(ahorrador.Id));
    }

    /// <summary>La nueva cuota ordinaria rige desde el inicio del periodo; el voluntario se conserva como estaba.</summary>
    private async Task RegistrarCambioCuotaAsync(Ahorrador ahorrador, Periodo periodo, decimal ordinaria, CancellationToken ct)
    {
        var desde = periodo.FechaInicio > ahorrador.FechaIngreso ? periodo.FechaInicio : ahorrador.FechaIngreso;
        var vigente = await db.CuotasAhorro.AsNoTracking()
            .Where(c => c.AhorradorId == ahorrador.Id && c.VigenteDesde <= periodo.FechaFin)
            .OrderByDescending(c => c.VigenteDesde).FirstOrDefaultAsync(ct);
        db.CuotasAhorro.Add(new CuotaAhorro
        {
            AhorradorId = ahorrador.Id, CuotaOrdinaria = ordinaria,
            CuotaVoluntaria = vigente?.CuotaVoluntaria ?? 0m, VoluntarioActivo = vigente?.VoluntarioActivo ?? false,
            VigenteDesde = desde
        });
    }

    public async Task EliminarIncidenciaAsync(int id, CancellationToken ct = default)
    {
        var permitidas = await acceso.CajasPermitidasAsync(ct);
        var incidencia = await db.Incidencias.Include(i => i.Periodo)
            .FirstOrDefaultAsync(i => i.Id == id && permitidas.Contains(i.Periodo.CajaId), ct)
            ?? throw ErrorNegocio.NoEncontrado(Codigos.IncidenciaNoEncontrada, "La incidencia no existe.");
        if (incidencia.Periodo.Estado != EstadoPeriodo.Abierto)
            throw ErrorNegocio.Conflicto(Codigos.PeriodoNoAbierto, "El periodo ya no está abierto.");
        // La baja y el cambio de cuota ya modificaron al ahorrador; se corrigen con una baja o una cuota nueva.
        if (incidencia.Tipo is TipoIncidencia.Baja or TipoIncidencia.CambioCuota)
            throw ErrorNegocio.Conflicto(Codigos.IncidenciaNoEliminable, "Esa incidencia ya cambió al ahorrador y no se elimina.");

        db.Incidencias.Remove(incidencia);
        await db.SaveChangesAsync(ct);
    }

    // ---------- Aplicar periodo ----------

    private sealed class FilaCuota
    {
        public long Id { get; set; }
        public int PrestamoId { get; set; }
        public int AhorradorId { get; set; }
        public int Numero { get; set; }
        public string Folio { get; set; } = "";
        public long Ultima { get; set; }
    }

    public async Task<AplicarPeriodoResultadoDto> AplicarPeriodoAsync(int periodoId, CancellationToken ct = default)
    {
        var (periodo, caja) = await CargarPeriodoAsync(periodoId, ct);
        if (periodo.Estado == EstadoPeriodo.Aplicado)
            throw ErrorNegocio.Conflicto(Codigos.PeriodoYaAplicado, "El periodo ya se aplicó.");
        if (periodo.Estado != EstadoPeriodo.Abierto)
            throw ErrorNegocio.Conflicto(Codigos.PeriodoNoAbierto, "El periodo está cerrado.");
        if (!caja.Activa)
            throw ErrorNegocio.Conflicto(Codigos.CajaInactiva, "La caja está inactiva.");
        if (await db.Periodos.AnyAsync(x => x.CajaId == caja.Id && x.Estado == EstadoPeriodo.Abierto && x.FechaInicio < periodo.FechaInicio, ct))
            throw ErrorNegocio.Conflicto(Codigos.PeriodoNoSiguiente, "Hay periodos anteriores sin aplicar; se aplican en orden.");

        var referencia = Periodicidad.Referencia(caja.FrecuenciaNomina, periodo.Numero, periodo.Anio);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // El paso de ABIERTO a APLICADO es la guarda de idempotencia: de dos peticiones simultáneas solo una
        // cambia el renglón y la otra recibe el conflicto.
        var cambiados = await db.EjecutarAsync(
            "UPDATE periodo SET estado = 'APLICADO' WHERE id = @id AND estado = 'ABIERTO'", new { id = periodoId }, ct);
        if (cambiados == 0)
            throw ErrorNegocio.Conflicto(Codigos.PeriodoYaAplicado, "El periodo ya se aplicó.");

        // Ahorro ordinario y voluntario: lo que calcula la vista, sin pasar por la memoria de la API.
        const string insertarAhorro = """
            INSERT INTO movimiento (caja_id, ahorrador_id, periodo_id, prestamo_id, fecha, tipo, monto, referencia)
            SELECT d.caja_id, d.ahorrador_id, d.periodo_id, NULL, @fecha, @tipo, d.{0}, @referencia
              FROM v_descuento_periodo d
             WHERE d.periodo_id = @periodo AND d.{0} > 0
            """;
        var fecha = periodo.FechaFin.ToDateTime(TimeOnly.MinValue);
        await db.EjecutarAsync(string.Format(insertarAhorro, "cuota_ordinaria"),
            new { fecha, referencia, periodo = periodoId, tipo = "AHORRO_ORDINARIO" }, ct);
        await db.EjecutarAsync(string.Format(insertarAhorro, "cuota_voluntaria"),
            new { fecha, referencia, periodo = periodoId, tipo = "AHORRO_VOLUNTARIO" }, ct);

        var suspendidos = (await db.ConsultarAsync<int>(
            "SELECT ahorrador_id FROM v_descuento_periodo WHERE periodo_id = @periodo AND suspendido = 1",
            new { periodo = periodoId }, ct)).ToHashSet();

        var cuotas = await db.ConsultarAsync<FilaCuota>(
            """
            SELECT am.id AS Id, am.prestamo_id AS PrestamoId, pr.ahorrador_id AS AhorradorId, am.numero AS Numero,
                   pr.folio AS Folio,
                   CAST(am.numero = (SELECT MAX(x.numero) FROM prestamo_amortizacion x WHERE x.prestamo_id = am.prestamo_id) AS SIGNED) AS Ultima
              FROM prestamo_amortizacion am
              JOIN prestamo pr ON pr.id = am.prestamo_id
             WHERE am.periodo_id = @periodo AND am.estado = 'PROGRAMADO' AND pr.estado = 'VIGENTE'
            """, new { periodo = periodoId }, ct);

        var aplicables = cuotas.Where(c => !suspendidos.Contains(c.AhorradorId)).ToList();
        var diferidas = cuotas.Where(c => suspendidos.Contains(c.AhorradorId)).ToList();
        var liquidados = 0;

        if (aplicables.Count > 0)
        {
            var ids = aplicables.Select(c => c.Id).ToList();
            var filas = await db.Amortizaciones.Include(a => a.Prestamo).Where(a => ids.Contains(a.Id)).ToListAsync(ct);
            foreach (var fila in filas)
            {
                var info = aplicables.First(c => c.Id == fila.Id);
                db.Movimientos.Add(NuevoMovimiento(fila.Prestamo, periodo, TipoMovimiento.AbonoCapital, fila.Capital, info.Folio));
                if (fila.Interes > 0m)
                    db.Movimientos.Add(NuevoMovimiento(fila.Prestamo, periodo, TipoMovimiento.AbonoInteres, fila.Interes, info.Folio));
                fila.Estado = EstadoAmortizacion.Aplicado;
                if (info.Ultima != 0)
                {
                    fila.Prestamo.Estado = EstadoPrestamo.Liquidado;
                    liquidados++;
                }
            }
        }

        if (diferidas.Count > 0) await DiferirCuotasAsync(caja, periodo, diferidas.Select(c => (c.PrestamoId, c.Numero)).ToList(), ct);

        await db.SaveChangesAsync(ct);

        // Quien estuvo con licencia o incapacidad en el periodo queda en LICENCIA; quien ya volvió, en ACTIVO.
        await db.EjecutarAsync(
            """
            UPDATE ahorrador a SET a.estado = 'LICENCIA'
             WHERE a.caja_id = @caja AND a.estado = 'ACTIVO'
               AND EXISTS (SELECT 1 FROM incidencia i WHERE i.ahorrador_id = a.id AND i.periodo_id = @periodo
                            AND i.tipo IN ('LICENCIA', 'INCAPACIDAD'))
            """, new { caja = caja.Id, periodo = periodoId }, ct);
        await db.EjecutarAsync(
            """
            UPDATE ahorrador a SET a.estado = 'ACTIVO'
             WHERE a.caja_id = @caja AND a.estado = 'LICENCIA'
               AND NOT EXISTS (SELECT 1 FROM incidencia i WHERE i.ahorrador_id = a.id AND i.periodo_id = @periodo
                                AND i.tipo IN ('LICENCIA', 'INCAPACIDAD'))
            """, new { caja = caja.Id, periodo = periodoId }, ct);

        var totales = await db.ConsultarAsync<TotalTipo>(
            """
            SELECT tipo AS Tipo, CAST(COUNT(*) AS SIGNED) AS Cuenta, CAST(COALESCE(SUM(monto), 0) AS DECIMAL(14,2)) AS Monto
              FROM movimiento WHERE periodo_id = @periodo GROUP BY tipo
            """, new { periodo = periodoId }, ct);
        decimal Suma(string tipo) => totales.FirstOrDefault(t => t.Tipo == tipo)?.Monto ?? 0m;

        var conDescuento = await db.EscalarAsync<int>(
            "SELECT CAST(COUNT(*) AS SIGNED) FROM v_descuento_periodo WHERE periodo_id = @periodo AND total > 0",
            new { periodo = periodoId }, ct);

        await tx.CommitAsync(ct);
        await _cache.InvalidarAsync(caja.Id);

        return new AplicarPeriodoResultadoDto(
            periodoId, referencia, conDescuento, suspendidos.Count, (int)totales.Sum(t => t.Cuenta),
            Suma("AHORRO_ORDINARIO"), Suma("AHORRO_VOLUNTARIO"), Suma("ABONO_CAPITAL"), Suma("ABONO_INTERES"),
            liquidados, diferidas.Count);
    }

    private sealed record TotalTipo(string Tipo, long Cuenta, decimal Monto);

    private static Movimiento NuevoMovimiento(Prestamo p, Periodo periodo, TipoMovimiento tipo, decimal monto, string folio) => new()
    {
        CajaId = p.CajaId, AhorradorId = p.AhorradorId, PeriodoId = periodo.Id, PrestamoId = p.Id,
        Fecha = periodo.FechaFin, Tipo = tipo, Monto = monto, Referencia = folio
    };

    /// <summary>
    /// Una licencia suspende todo el descuento, también la cuota del préstamo: esa cuota y las que le siguen se
    /// recorren un periodo (mismos importes), así el préstamo no queda con una cuota sin cobrar para siempre.
    /// </summary>
    private async Task DiferirCuotasAsync(Caja caja, Periodo periodo, List<(int PrestamoId, int Numero)> afectadas, CancellationToken ct)
    {
        foreach (var (prestamoId, numero) in afectadas)
        {
            var restantes = await db.Amortizaciones
                .Where(a => a.PrestamoId == prestamoId && a.Numero >= numero && a.Estado == EstadoAmortizacion.Programado)
                .OrderBy(a => a.Numero).ToListAsync(ct);
            var siguientes = await _calendario.SiguientesAsync(caja, periodo, restantes.Count, crear: true, ct);

            // Las cuotas pendientes ocupan periodos consecutivos desde este: cada una pasa al periodo que sigue.
            for (var i = 0; i < restantes.Count; i++)
            {
                restantes[i].PeriodoId = siguientes[i].Id;
                restantes[i].Fecha = siguientes[i].FechaFin;
            }
        }
    }

    // ---------- Layout de nómina ----------

    private sealed record FilaLayout(string NumEmpleado, decimal Ordinaria, decimal Voluntaria, decimal Prestamo);

    public async Task<ArchivoGenerado> GenerarLayoutAsync(int periodoId, FormatoLayout formato, CancellationToken ct = default)
    {
        var (periodo, caja) = await CargarPeriodoAsync(periodoId, ct);
        var filas = await db.ConsultarAsync<FilaLayout>(
            """
            SELECT a.num_empleado AS NumEmpleado, d.cuota_ordinaria AS Ordinaria, d.cuota_voluntaria AS Voluntaria,
                   d.cuota_prestamo AS Prestamo
              FROM v_descuento_periodo d
              JOIN ahorrador a ON a.id = d.ahorrador_id
             WHERE d.periodo_id = @periodo AND d.suspendido = 0 AND d.total > 0
             ORDER BY a.num_empleado
            """, new { periodo = periodoId }, ct);

        var lineas = new List<LineaLayout>();
        foreach (var f in filas)
        {
            if (f.Ordinaria > 0m) lineas.Add(new LineaLayout(f.NumEmpleado, LayoutNomina.ConceptoOrdinario, f.Ordinaria));
            if (f.Voluntaria > 0m) lineas.Add(new LineaLayout(f.NumEmpleado, LayoutNomina.ConceptoVoluntario, f.Voluntaria));
            if (f.Prestamo > 0m) lineas.Add(new LineaLayout(f.NumEmpleado, LayoutNomina.ConceptoPrestamo, f.Prestamo));
        }
        if (lineas.Count == 0)
            throw ErrorNegocio.Conflicto(Codigos.LayoutSinRegistros, "El periodo no tiene descuentos que enviar a nómina.");

        var referencia = Periodicidad.Referencia(caja.FrecuenciaNomina, periodo.Numero, periodo.Anio);
        var tipo = Periodicidad.TipoPeriodo(caja.FrecuenciaNomina);
        var contenido = LayoutNomina.Generar(formato, lineas, caja.Prefijo, referencia, tipo);
        var control = LayoutNomina.SumaControl(lineas, referencia, tipo);
        var importe = lineas.Sum(l => l.Importe);
        var nombre = $"nomina_{caja.Prefijo}_{referencia.Replace('-', '_')}.{LayoutNomina.Extension(formato)}";

        db.Layouts.Add(new LayoutGenerado
        {
            Tipo = TipoLayout.Nomina, CajaId = caja.Id, PeriodoId = periodo.Id, Formato = LayoutNomina.NombreFormato(formato),
            NombreArchivo = nombre, Registros = lineas.Count, ImporteTotal = importe, SumaControl = control,
            UsuarioId = usuario.Id, Fecha = reloj.GetUtcNow().UtcDateTime
        });
        await db.SaveChangesAsync(ct);

        return new ArchivoGenerado(contenido, nombre, LayoutNomina.TipoContenido(formato), lineas.Count, importe, control);
    }

    private sealed record FilaLayoutHistorial(
        int Id, string Tipo, int CajaId, int? PeriodoId, string? Periodo, string Formato, string NombreArchivo,
        int Registros, decimal ImporteTotal, string? SumaControl, string Usuario, DateTime Fecha);

    public async Task<Pagina<LayoutDto>> ListarLayoutsAsync(ConsultaLayouts consulta, CancellationToken ct = default)
    {
        IReadOnlyCollection<int> cajas = await acceso.CajasPermitidasAsync(ct);
        if (consulta.CajaId is { } cajaId)
        {
            await acceso.ExigirAsync(cajaId, ct);
            cajas = [cajaId];
        }
        if (cajas.Count == 0) return new Pagina<LayoutDto>([], consulta.PaginaSegura, consulta.TamanoSeguro, 0);

        var where = new StringBuilder("l.caja_id IN @cajas AND l.tipo = 'NOMINA'");
        var p = new Dapper.DynamicParameters();
        p.Add("cajas", cajas);
        if (consulta.PeriodoId is { } periodo) { where.Append(" AND l.periodo_id = @periodo"); p.Add("periodo", periodo); }
        p.Add("tomar", consulta.TamanoSeguro);
        p.Add("saltar", consulta.Saltar);

        var total = await db.EscalarAsync<int>($"SELECT CAST(COUNT(*) AS SIGNED) FROM layout_generado l WHERE {where}", p, ct);
        var filas = await db.ConsultarAsync<FilaLayoutHistorial>(
            $"""
            SELECT l.id AS Id, l.tipo AS Tipo, l.caja_id AS CajaId, l.periodo_id AS PeriodoId,
                   CONCAT(IF(c.frecuencia_nomina = 'QUINCENAL', 'Q', 'S'), pe.numero, '-', pe.anio) AS Periodo,
                   l.formato AS Formato, l.nombre_archivo AS NombreArchivo, l.registros AS Registros,
                   l.importe_total AS ImporteTotal, l.suma_control AS SumaControl, u.nombre AS Usuario, l.fecha AS Fecha
              FROM layout_generado l
              JOIN caja c ON c.id = l.caja_id
              JOIN usuario u ON u.id = l.usuario_id
              LEFT JOIN periodo pe ON pe.id = l.periodo_id
             WHERE {where}
             ORDER BY l.fecha DESC, l.id DESC
             LIMIT @tomar OFFSET @saltar
            """, p, ct);

        var items = filas.Select(f => new LayoutDto(f.Id, ConversionesEnum.DeDb<TipoLayout>(f.Tipo), f.CajaId, f.PeriodoId, f.Periodo,
            f.Formato, f.NombreArchivo, f.Registros, f.ImporteTotal, f.SumaControl, f.Usuario, f.Fecha)).ToList();
        return new Pagina<LayoutDto>(items, consulta.PaginaSegura, consulta.TamanoSeguro, total);
    }
}

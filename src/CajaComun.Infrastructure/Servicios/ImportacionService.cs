using System.Text.Json;
using CajaComun.Application.Comun;
using CajaComun.Application.Importaciones;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;
using CajaComun.Infrastructure.Archivos;
using CajaComun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace CajaComun.Infrastructure.Servicios;

public sealed class ImportacionService(
    CajaComunDbContext db, ICajaAcceso acceso, IUsuarioActual usuario, TimeProvider reloj, ICacheDatos? cache = null) : IImportacionService
{
    private readonly ICacheDatos _cache = cache ?? new SinCache();
    private readonly CalendarioPeriodos _calendario = new(db);
    private readonly BajaAhorrador _baja = new(db);

    private DateOnly Hoy => DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime);

    private sealed record FilaAnalizada(
        int Numero, TipoCambioImportacion Tipo, string NumEmpleado, IReadOnlyList<ErrorFila> Errores, DatosAlta? Alta,
        DatosBaja? Baja, Ahorrador? Existente)
    {
        public bool ConError => Errores.Count > 0;
    }

    // ---------- Carga y vista previa ----------

    public async Task<VistaPreviaImportacionDto> CargarAsync(
        int cajaId, string nombreArchivo, Stream contenido, CancellationToken ct = default)
    {
        await acceso.ExigirAsync(cajaId, ct);
        var caja = await db.Cajas.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cajaId, ct)
            ?? throw ErrorNegocio.NoEncontrado(Codigos.CajaNoEncontrada, "La caja no existe.");
        if (!caja.Activa) throw ErrorNegocio.Conflicto(Codigos.CajaInactiva, "La caja está inactiva y no admite altas.");

        var nombre = Path.GetFileName(nombreArchivo ?? "");
        if (nombre.Length > 255) nombre = nombre[^255..];
        var bytes = await LeerAcotadoAsync(contenido, ct);
        var leido = LectorImportacion.Leer(nombre, bytes);

        var faltantes = new[] { "movimiento", "num_empleado" }.Where(c => !leido.Columnas.Contains(c)).ToArray();
        if (faltantes.Length > 0)
            throw new ErrorNegocio(Codigos.ArchivoInvalido, "Faltan columnas obligatorias en el encabezado.", 400,
                new Dictionary<string, string[]> { ["columnas"] = faltantes.Select(c => "COLUMNA_FALTANTE:" + c).ToArray() });

        var filas = leido.Filas.Select(f => (f.Numero, (IReadOnlyDictionary<string, string?>)f.Datos)).ToList();
        var analizadas = await AnalizarAsync(caja, filas, rastrear: false, ct);

        var lote = new Importacion
        {
            CajaId = cajaId, Archivo = nombre, Formato = leido.Formato, UsuarioId = usuario.Id,
            Fecha = reloj.GetUtcNow().UtcDateTime, Estado = EstadoImportacion.Previa
        };
        foreach (var (a, original) in analizadas.Zip(leido.Filas))
        {
            lote.Detalles.Add(new ImportacionDetalle
            {
                NumeroFila = a.Numero, Tipo = a.Tipo, NumEmpleado = a.NumEmpleado.Length > 20 ? a.NumEmpleado[..20] : a.NumEmpleado,
                Datos = JsonSerializer.Serialize(Conocidas(original.Datos)),
                Validacion = a.ConError ? ResultadoValidacion.Error : ResultadoValidacion.Valido,
                MensajeError = a.ConError ? Truncar(string.Join(';', a.Errores.Select(e => $"{e.Campo}:{e.Codigo}")), 500) : null
            });
        }
        db.Importaciones.Add(lote);
        await db.SaveChangesAsync(ct);
        return await ObtenerAsync(lote.Id, new ConsultaDetalleImportacion(), ct);
    }

    private static async Task<byte[]> LeerAcotadoAsync(Stream contenido, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[81_920];
        int leidos;
        while ((leidos = await contenido.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, leidos);
            if (ms.Length > LimitesImportacion.TamanoMaximoBytes)
                throw new ErrorNegocio(Codigos.ArchivoDemasiadoGrande, "El archivo excede el tamaño máximo permitido.", 413);
        }
        return ms.ToArray();
    }

    private static Dictionary<string, string?> Conocidas(IReadOnlyDictionary<string, string?> datos) =>
        ValidadorFilaImportacion.Columnas.Where(datos.ContainsKey).ToDictionary(c => c, c => datos[c]);

    private static string Truncar(string texto, int max) => texto.Length <= max ? texto : texto[..max];

    // ---------- Análisis (se usa en la vista previa y otra vez al aplicar) ----------

    private async Task<List<FilaAnalizada>> AnalizarAsync(
        Caja caja, IReadOnlyList<(int Numero, IReadOnlyDictionary<string, string?> Datos)> filas, bool rastrear, CancellationToken ct)
    {
        var validadas = filas.Select(f => (f.Numero, V: ValidadorFilaImportacion.Validar(f.Datos, caja.Prefijo, Hoy))).ToList();

        var numeros = validadas.Where(v => v.V.NumEmpleado != "" && ValidadorFilaImportacion.NumeroValido(v.V.NumEmpleado))
            .Select(v => v.V.NumEmpleado).Distinct().ToList();
        var consulta = db.Ahorradores.Where(a => a.CajaId == caja.Id && numeros.Contains(a.NumEmpleado));
        var existentes = (rastrear ? await consulta.ToListAsync(ct) : await consulta.AsNoTracking().ToListAsync(ct))
            .ToDictionary(a => a.NumEmpleado);

        var primerAbierto = await _calendario.PrimerAbiertoAsync(caja.Id, ct);
        var vistos = new HashSet<string>();
        var resultado = new List<FilaAnalizada>(validadas.Count);

        // Quién tendría una baja bloqueada se consulta una sola vez para todo el archivo.
        var candidatosBaja = validadas
            .Where(v => v.V.Baja is not null && existentes.TryGetValue(v.V.NumEmpleado, out var e) && e.Estado != EstadoAhorrador.Baja)
            .Select(v => existentes[v.V.NumEmpleado].Id).ToList();
        var bloqueos = await _baja.BloqueosAsync(candidatosBaja, ct);

        foreach (var (numero, v) in validadas)
        {
            var errores = v.Errores.ToList();
            existentes.TryGetValue(v.NumEmpleado, out var existente);
            var tipo = v.Movimiento ?? TipoCambioImportacion.SinCambio;

            if (v.NumEmpleado != "" && !vistos.Add(v.NumEmpleado))
                errores.Add(new ErrorFila("num_empleado", "DUPLICADO_EN_ARCHIVO"));

            if (errores.Count == 0 && v.Alta is { } alta && existente is not null)
            {
                // Volver a cargar el mismo alta no cambia nada; el mismo número con otra persona es un conflicto.
                if (existente.Estado != EstadoAhorrador.Baja && existente.Rfc == alta.Rfc) tipo = TipoCambioImportacion.SinCambio;
                else errores.Add(new ErrorFila("num_empleado", "NUM_EMPLEADO_DUPLICADO"));
            }

            if (errores.Count == 0 && v.Baja is { } baja)
            {
                if (existente is null) errores.Add(new ErrorFila("num_empleado", "EMPLEADO_INEXISTENTE"));
                else if (existente.Estado == EstadoAhorrador.Baja) tipo = TipoCambioImportacion.SinCambio;
                else if (primerAbierto is null) errores.Add(new ErrorFila("fecha_baja", Codigos.SinPeriodoAbierto));
                else if (BajaAhorrador.ValidarFecha(existente, baja.FechaBaja, primerAbierto) is { } codigo)
                    errores.Add(new ErrorFila("fecha_baja", codigo));
                else if (bloqueos.TryGetValue(existente.Id, out var bloqueo))
                    errores.Add(new ErrorFila("num_empleado", bloqueo));
            }

            resultado.Add(new FilaAnalizada(numero, tipo, v.NumEmpleado, errores, v.Alta, v.Baja, existente));
        }
        return resultado;
    }

    // ---------- Consulta ----------

    private async Task<Dictionary<int, ResumenImportacionDto>> ResumenesAsync(IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        var cuentas = await db.ImportacionDetalles.AsNoTracking()
            .Where(d => ids.Contains(d.ImportacionId))
            .GroupBy(d => new { d.ImportacionId, d.Tipo, d.Validacion })
            .Select(g => new { g.Key.ImportacionId, g.Key.Tipo, g.Key.Validacion, Cuenta = g.Count() })
            .ToListAsync(ct);

        return ids.ToDictionary(id => id, id =>
        {
            var mias = cuentas.Where(c => c.ImportacionId == id).ToList();
            int Cuenta(Func<TipoCambioImportacion, ResultadoValidacion, bool> f) =>
                mias.Where(c => f(c.Tipo, c.Validacion)).Sum(c => c.Cuenta);
            return new ResumenImportacionDto(
                mias.Sum(c => c.Cuenta),
                Cuenta((t, v) => v == ResultadoValidacion.Valido && t == TipoCambioImportacion.Alta),
                Cuenta((t, v) => v == ResultadoValidacion.Valido && t == TipoCambioImportacion.Baja),
                Cuenta((t, v) => v == ResultadoValidacion.Valido && t == TipoCambioImportacion.SinCambio),
                Cuenta((_, v) => v == ResultadoValidacion.Error));
        });
    }

    private sealed record FilaLote(
        int Id, int CajaId, string Caja, string Archivo, FormatoArchivo Formato, string Usuario, DateTime Fecha, EstadoImportacion Estado);

    private IQueryable<Importacion> Lotes(IReadOnlyCollection<int> cajas) =>
        db.Importaciones.AsNoTracking().Where(i => cajas.Contains(i.CajaId));

    // Se filtra y ordena sobre la entidad; la proyección va al final porque EF no filtra sobre un constructor.
    private static readonly System.Linq.Expressions.Expression<Func<Importacion, FilaLote>> ALote =
        i => new FilaLote(i.Id, i.CajaId, i.Caja.Nombre, i.Archivo, i.Formato, i.Usuario.Nombre, i.Fecha, i.Estado);

    public async Task<Pagina<ImportacionDto>> ListarAsync(ConsultaImportaciones consulta, CancellationToken ct = default)
    {
        IReadOnlyCollection<int> cajas = await acceso.CajasPermitidasAsync(ct);
        if (consulta.CajaId is { } cajaId)
        {
            await acceso.ExigirAsync(cajaId, ct);
            cajas = [cajaId];
        }
        var q = Lotes(cajas);
        if (consulta.Estado is { } estado) q = q.Where(l => l.Estado == estado);

        var total = await q.CountAsync(ct);
        var pagina = await q.OrderByDescending(l => l.Fecha).ThenByDescending(l => l.Id)
            .Skip(consulta.Saltar).Take(consulta.TamanoSeguro).Select(ALote).ToListAsync(ct);
        var resumenes = await ResumenesAsync(pagina.Select(l => l.Id).ToList(), ct);
        return new Pagina<ImportacionDto>(
            pagina.Select(l => Mapear(l, resumenes[l.Id])).ToList(), consulta.PaginaSegura, consulta.TamanoSeguro, total);
    }

    private static ImportacionDto Mapear(FilaLote l, ResumenImportacionDto resumen) =>
        new(l.Id, l.CajaId, l.Caja, l.Archivo, l.Formato, l.Usuario, l.Fecha, l.Estado, resumen);

    private async Task<FilaLote> CargarLoteAsync(int id, CancellationToken ct)
    {
        var permitidas = await acceso.CajasPermitidasAsync(ct);
        return await Lotes(permitidas).Where(l => l.Id == id).Select(ALote).FirstOrDefaultAsync(ct)
            ?? throw ErrorNegocio.NoEncontrado(Codigos.ImportacionNoEncontrada, "La importación no existe.");
    }

    public async Task<VistaPreviaImportacionDto> ObtenerAsync(int id, ConsultaDetalleImportacion consulta, CancellationToken ct = default)
    {
        var lote = await CargarLoteAsync(id, ct);
        var resumen = (await ResumenesAsync([id], ct))[id];

        var q = db.ImportacionDetalles.AsNoTracking().Where(d => d.ImportacionId == id);
        if (consulta.Validacion is { } v) q = q.Where(d => d.Validacion == v);
        if (consulta.Tipo is { } t) q = q.Where(d => d.Tipo == t);
        var total = await q.CountAsync(ct);
        var filas = await q.OrderBy(d => d.NumeroFila).Skip(consulta.Saltar).Take(consulta.TamanoSeguro).ToListAsync(ct);

        var items = filas.Select(d => new ImportacionDetalleDto(
            d.Id, d.NumeroFila, d.Tipo, d.NumEmpleado, d.Validacion, ErroresDe(d.MensajeError),
            JsonSerializer.Deserialize<Dictionary<string, string?>>(d.Datos) ?? [])).ToList();
        return new VistaPreviaImportacionDto(Mapear(lote, resumen),
            new Pagina<ImportacionDetalleDto>(items, consulta.PaginaSegura, consulta.TamanoSeguro, total));
    }

    private static IReadOnlyList<ErrorFila> ErroresDe(string? mensaje) =>
        string.IsNullOrEmpty(mensaje)
            ? []
            : mensaje.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Split(':', 2))
                .Select(p => new ErrorFila(p[0], p.Length > 1 ? p[1] : ""))
                .ToList();

    // ---------- Aplicar y cancelar ----------

    public async Task<AplicarImportacionResultadoDto> AplicarAsync(int id, bool omitirErrores, CancellationToken ct = default)
    {
        var lote = await CargarLoteAsync(id, ct);
        if (lote.Estado != EstadoImportacion.Previa)
            throw ErrorNegocio.Conflicto(Codigos.ImportacionYaProcesada, "La importación ya se aplicó o se canceló.");
        var caja = await db.Cajas.AsNoTracking().FirstAsync(c => c.Id == lote.CajaId, ct);
        if (!caja.Activa) throw ErrorNegocio.Conflicto(Codigos.CajaInactiva, "La caja está inactiva y no admite altas.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        // De dos peticiones simultáneas solo una cambia el estado del lote; la otra recibe el conflicto.
        var cambiados = await db.EjecutarAsync(
            "UPDATE importacion SET estado = 'APLICADA' WHERE id = @id AND estado = 'PREVIA'", new { id }, ct);
        if (cambiados == 0)
            throw ErrorNegocio.Conflicto(Codigos.ImportacionYaProcesada, "La importación ya se aplicó o se canceló.");

        // Se vuelve a validar contra el padrón de este momento: pudo cambiar desde la vista previa.
        var detalles = await db.ImportacionDetalles.AsNoTracking().Where(d => d.ImportacionId == id)
            .OrderBy(d => d.NumeroFila).ToListAsync(ct);
        var filas = detalles
            .Select(d => (d.NumeroFila, (IReadOnlyDictionary<string, string?>)(JsonSerializer.Deserialize<Dictionary<string, string?>>(d.Datos) ?? [])))
            .ToList();
        var analizadas = await AnalizarAsync(caja, filas, rastrear: true, ct);

        var conError = analizadas.Count(a => a.ConError);
        if (conError > 0 && !omitirErrores)
            throw new ErrorNegocio(Codigos.ImportacionConErrores,
                $"El lote tiene {conError} renglones con errores; corrige el archivo o aplica omitiéndolos.", 409);

        var primerAbierto = await _calendario.PrimerAbiertoAsync(caja.Id, ct);
        var altas = 0;
        var bajas = 0;
        var sinCambio = 0;
        decimal liquidado = 0m;

        var aBaja = analizadas.Where(a => !a.ConError && a.Tipo == TipoCambioImportacion.Baja && a.Baja is not null).ToList();
        var saldos = await _baja.SaldosAsync(aBaja.Select(a => a.Existente!.Id).ToList(), ct);

        foreach (var a in analizadas.Where(a => !a.ConError))
        {
            switch (a.Tipo)
            {
                case TipoCambioImportacion.Alta when a.Alta is { } d:
                    NuevoAhorrador(caja, d, primerAbierto);
                    altas++;
                    break;
                case TipoCambioImportacion.Baja when a.Baja is { } b:
                    liquidado += _baja.Ejecutar(a.Existente!, b.FechaBaja, saldos.GetValueOrDefault(a.Existente!.Id));
                    bajas++;
                    break;
                default:
                    sinCambio++;
                    break;
            }
        }

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
        {
            // Otra captura creó el mismo número entre la validación y el guardado: la llave única decide.
            throw ErrorNegocio.Conflicto(Codigos.NumEmpleadoDuplicado, "Un número de empleado del lote ya existe en la caja.");
        }
        await tx.CommitAsync(ct);
        await _cache.InvalidarAsync(caja.Id);
        return new AplicarImportacionResultadoDto(id, altas, bajas, sinCambio, conError, liquidado);
    }

    /// <summary>
    /// La primera cuota rige desde el ingreso o, si es anterior, desde el primer periodo abierto: no hubo descuentos
    /// en los periodos ya aplicados y la vista de descuentos no debe inventarlos.
    /// </summary>
    private void NuevoAhorrador(Caja caja, DatosAlta d, Periodo? primerAbierto)
    {
        var ahorrador = new Ahorrador
        {
            CajaId = caja.Id, NumEmpleado = d.NumEmpleado, Nombre = d.Nombre, Apellidos = d.Apellidos, Rfc = d.Rfc,
            FechaNacimiento = d.FechaNacimiento, FechaIngreso = d.FechaIngreso, Estado = EstadoAhorrador.Activo,
            SueldoQuincenal = d.SueldoQuincenal, Clabe = d.Clabe, Departamento = d.Departamento
        };
        db.Ahorradores.Add(ahorrador);
        db.CuotasAhorro.Add(new CuotaAhorro
        {
            Ahorrador = ahorrador, CuotaOrdinaria = d.CuotaOrdinaria,
            CuotaVoluntaria = d.VoluntarioActivo ? d.CuotaVoluntaria : 0m, VoluntarioActivo = d.VoluntarioActivo,
            VigenteDesde = primerAbierto is { } p && p.FechaInicio > d.FechaIngreso ? p.FechaInicio : d.FechaIngreso
        });
    }

    public async Task CancelarAsync(int id, CancellationToken ct = default)
    {
        var lote = await CargarLoteAsync(id, ct);
        var cambiados = await db.EjecutarAsync(
            "UPDATE importacion SET estado = 'CANCELADA' WHERE id = @id AND estado = 'PREVIA'", new { id = lote.Id }, ct);
        if (cambiados == 0)
            throw ErrorNegocio.Conflicto(Codigos.ImportacionYaProcesada, "La importación ya se aplicó o se canceló.");
    }
}

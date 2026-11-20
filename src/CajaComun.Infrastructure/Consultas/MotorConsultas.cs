using System.Diagnostics;
using System.Globalization;
using System.Text;
using CajaComun.Application.Comun;
using CajaComun.Application.Consultas;
using CajaComun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CajaComun.Infrastructure.Consultas;

/// <summary>Cómo se repartieron las cajas entre los motores. Debe coincidir con las consultas de docs/consultas-parte2.sql.</summary>
public static class DistribucionMotores
{
    public const string MariaDb = "MariaDB";
    public const string PostgreSql = "PostgreSQL";
    public const string ClickHouse = "ClickHouse";

    /// <summary>Prefijos de las cajas que se pasaron a otro motor; las demás se quedan en MariaDB.</summary>
    public static string MotorDe(string prefijo) => prefijo switch
    {
        "ACE" => PostgreSql,
        "TDB" => ClickHouse,
        _ => MariaDb,
    };

    public const string FiltroMariaDb = "caja_id NOT IN (SELECT id FROM mariadb.caja_comun.caja WHERE prefijo IN ('ACE', 'TDB'))";
}

/// <summary>
/// Ejecuta las consultas de la Parte 2 sin revisar permisos (eso lo hace <see cref="ConsultasDistribuidasService"/>), para
/// que los comandos de línea de comandos puedan usarla sin una sesión.
/// </summary>
public sealed class MotorConsultas(ITrinoClient trino, IFabricaConexion conexiones, CajaComunDbContext db, TimeProvider reloj)
    : IConsultasDistribuidasService
{
    private const int MaxFilasExportacion = 200_000;
    // La hora de la Ciudad de México es UTC-6 todo el año (sin horario de verano desde 2022).
    private static readonly TimeSpan HoraMexico = TimeSpan.FromHours(-6);

    public async Task<EstadoFederacionDto> EstadoAsync(CancellationToken ct = default)
    {
        const string sql = $"""
            SELECT 'MariaDB' AS motor,
                   (SELECT count(*) FROM mariadb.caja_comun.ahorrador WHERE {DistribucionMotores.FiltroMariaDb}) AS ahorradores,
                   (SELECT count(*) FROM mariadb.caja_comun.movimiento WHERE {DistribucionMotores.FiltroMariaDb}) AS movimientos
            UNION ALL
            SELECT 'PostgreSQL', (SELECT count(*) FROM postgresql.public.ahorrador), (SELECT count(*) FROM postgresql.public.movimiento)
            UNION ALL
            SELECT 'ClickHouse', (SELECT count(*) FROM clickhouse.bravo.ahorrador), (SELECT count(*) FROM clickhouse.bravo.movimiento)
            """;
        try
        {
            // Sin reintentos: es la comprobación de "¿está encendida?" y debe contestar pronto.
            var r = await trino.EjecutarAsync(sql, reintentar: false, ct);
            var cajas = await db.Cajas.AsNoTracking().OrderBy(c => c.Nombre).Select(c => new { c.Prefijo, c.Nombre }).ToListAsync(ct);
            var motores = r.Filas.Select(f =>
            {
                var motor = (string)f[0]!;
                var nombres = cajas.Where(c => DistribucionMotores.MotorDe(c.Prefijo) == motor).Select(c => c.Nombre);
                var catalogo = motor switch { DistribucionMotores.PostgreSql => "postgresql", DistribucionMotores.ClickHouse => "clickhouse", _ => "mariadb" };
                return new MotorDto(motor, catalogo, string.Join(", ", nombres), Convert.ToInt32(f[1], CultureInfo.InvariantCulture), Convert.ToInt32(f[2], CultureInfo.InvariantCulture));
            }).ToList();
            return new EstadoFederacionDto(true, null, motores, CatalogoConsultas.Definiciones);
        }
        catch (ErrorNegocio e) when (e.Codigo is Codigos.FederacionNoDisponible or Codigos.ConsultaFederadaFallo)
        {
            return new EstadoFederacionDto(false, e.Codigo, [], CatalogoConsultas.Definiciones);
        }
    }

    public async Task<ResultadoConsulta> EjecutarAsync(int consulta, int? anio, int page, int pageSize, CancellationToken ct = default)
    {
        var def = Definir(consulta);
        var (anioEfectivo, corte) = await ParametrosAsync(def, anio, ct);
        var sql = CatalogoConsultas.Federada(def, anioEfectivo, corte);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, ConsultaPaginada.PageSizeMaximo);

        var cronometro = Stopwatch.StartNew();
        var total = trino.EjecutarAsync($"SELECT count(*) FROM (\n{sql}\n)", ct: ct);
        var pagina = trino.EjecutarAsync($"{sql}\nOFFSET {(page - 1) * pageSize} LIMIT {pageSize}", ct: ct);
        await Task.WhenAll(total, pagina);
        cronometro.Stop();

        var filas = pagina.Result.Filas.Select(f => (IReadOnlyList<object?>)f).ToList();
        return new ResultadoConsulta(consulta, anioEfectivo, sql, pagina.Result.Columnas, filas,
            Convert.ToInt32(total.Result.Filas[0][0], CultureInfo.InvariantCulture), page, pageSize, cronometro.ElapsedMilliseconds);
    }

    public async Task<(string Nombre, byte[] Contenido)> ExportarCsvAsync(int consulta, int? anio, CancellationToken ct = default)
    {
        var def = Definir(consulta);
        var (anioEfectivo, corte) = await ParametrosAsync(def, anio, ct);
        var r = await trino.EjecutarAsync(CatalogoConsultas.Federada(def, anioEfectivo, corte), ct: ct);
        if (r.Filas.Count > MaxFilasExportacion)
            throw new ErrorNegocio(Codigos.Validacion, "El resultado es demasiado grande para exportarlo.");

        var sb = new StringBuilder();
        sb.AppendJoin(',', r.Columnas.Select(c => Csv(c.Nombre))).Append("\r\n");
        foreach (var fila in r.Filas)
            sb.AppendJoin(',', fila.Select(v => Csv(Texto(v)))).Append("\r\n");

        var nombre = anioEfectivo is { } a && def.UsaAnio ? $"consulta-q{consulta}-{a}.csv" : $"consulta-q{consulta}.csv";
        // BOM para que Excel abra bien los acentos.
        return (nombre, new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray());
    }

    public async Task<EquivalenciaDto> VerificarEquivalenciaAsync(int consulta, int? anio, CancellationToken ct = default)
    {
        var def = Definir(consulta);
        var (anioEfectivo, corte) = await ParametrosAsync(def, anio, ct);
        var sqlFederado = CatalogoConsultas.Federada(def, anioEfectivo, corte);
        var sqlDirecto = CatalogoConsultas.Directa(def, anioEfectivo, corte);

        var federada = await trino.EjecutarAsync(sqlFederado, ct: ct);
        var relojDirecto = Stopwatch.StartNew();
        var directas = await LeerDirectoAsync(sqlDirecto, ct);
        relojDirecto.Stop();

        var (diferencias, ejemplo) = ComparadorFilas.Comparar(federada.Filas, directas, federada.Columnas.Select(c => c.Tipo).ToList());

        return new EquivalenciaDto(consulta, anioEfectivo, federada.Filas.Count, directas.Count, diferencias == 0, diferencias, ejemplo,
            sqlDirecto, federada.DuracionMs, relojDirecto.ElapsedMilliseconds);
    }

    // --- apoyo ---

    private static ConsultaDefinicion Definir(int consulta) =>
        CatalogoConsultas.Buscar(consulta)
        ?? throw ErrorNegocio.NoEncontrado(Codigos.ConsultaNoEncontrada, "No existe esa consulta (usa de la 1 a la 5).");

    private async Task<(int? Anio, DateOnly Corte)> ParametrosAsync(ConsultaDefinicion def, int? anio, CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(reloj.GetUtcNow().ToOffset(HoraMexico).DateTime);
        if (!def.UsaAnio) return (null, hoy);

        if (anio is null)
        {
            // Sin año se toma el más reciente que tenga ciclo; si aún no hay ciclos, el año en curso.
            anio = await db.Ciclos.AsNoTracking().MaxAsync(c => (int?)c.Anio, ct) ?? hoy.Year;
        }
        if (anio is < 2000 or > 2100)
            throw new ErrorNegocio(Codigos.Validacion, "El año debe estar entre 2000 y 2100.",
                detalles: new Dictionary<string, string[]> { ["anio"] = ["VALOR_INVALIDO"] });
        return (anio, hoy);
    }

    private async Task<List<object?[]>> LeerDirectoAsync(string sql, CancellationToken ct)
    {
        await using var cx = await conexiones.AbrirAsync(ct);
        await using var cmd = cx.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 90;
        await using var lector = await cmd.ExecuteReaderAsync(ct);
        var filas = new List<object?[]>();
        while (await lector.ReadAsync(ct))
        {
            var fila = new object?[lector.FieldCount];
            for (var i = 0; i < fila.Length; i++) fila[i] = lector.IsDBNull(i) ? null : lector.GetValue(i);
            filas.Add(fila);
        }
        return filas;
    }

    private static string Texto(object? v) => v switch
    {
        null => "",
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    private static string Csv(string valor)
    {
        // Un texto que empieza con = + - @ lo evalúa Excel como fórmula: se antepone una comilla simple.
        if (valor.Length > 0 && valor[0] is '=' or '+' or '-' or '@' && !decimal.TryParse(valor, NumberStyles.Any, CultureInfo.InvariantCulture, out _))
            valor = "'" + valor;
        return valor.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{valor.Replace("\"", "\"\"")}\"" : valor;
    }
}

/// <summary>
/// Compara dos conjuntos de filas venga cada uno del motor que venga. El orden no importa (depende de la colación de cada
/// motor); sí importan las repeticiones. Los valores se llevan a una forma única antes de compararlos: importes con
/// dos decimales, enteros sin decimales y fechas AAAA-MM-DD, para comparar el valor y no su representación.
/// </summary>
public static class ComparadorFilas
{
    public static (int Diferencias, string? Ejemplo) Comparar(
        IReadOnlyList<object?[]> federadas, IReadOnlyList<object?[]> directas, IReadOnlyList<string> tipos)
    {
        var conteo = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in federadas) { var c = Canonica(f, tipos); conteo[c] = conteo.GetValueOrDefault(c) + 1; }
        foreach (var f in directas) { var c = Canonica(f, tipos); conteo[c] = conteo.GetValueOrDefault(c) - 1; }
        var distintas = conteo.Where(kv => kv.Value != 0).ToList();
        var diferencias = distintas.Sum(kv => Math.Abs(kv.Value));
        var ejemplo = distintas.Count == 0 ? null
            : $"{(distintas[0].Value > 0 ? "solo en la federada" : "solo en MariaDB")}: {distintas[0].Key.Replace('', ' ')}";
        return (diferencias, ejemplo);
    }

    /// <summary>
    /// Forma única de escribir una fila venga del motor que venga: importes con dos decimales, enteros sin
    /// decimales, fechas AAAA-MM-DD. Así se compara el valor y no su representación.
    /// </summary>
    public static string Canonica(object?[] fila, IReadOnlyList<string> tipos) =>
        string.Join('\u001f', fila.Select((v, i) => CanonicaCelda(v, tipos[i])));

    private static string CanonicaCelda(object? v, string tipo) => v switch
    {
        null => "∅",
        DateTime d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        decimal m => m.ToString("0.00", CultureInfo.InvariantCulture),
        string s when tipo.StartsWith("decimal", StringComparison.Ordinal) =>
            decimal.Parse(s, CultureInfo.InvariantCulture).ToString("0.00", CultureInfo.InvariantCulture),
        string s => s,
        IConvertible c => c.ToString(CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

}

/// <summary>Capa con permisos: las consultas abarcan todas las cajas, así que solo las ve quien tiene acceso a todas.</summary>
public sealed class ConsultasDistribuidasService(MotorConsultas motor, ICajaAcceso acceso, CajaComunDbContext db)
    : IConsultasDistribuidasService
{
    public async Task<EstadoFederacionDto> EstadoAsync(CancellationToken ct = default)
    {
        await ExigirTodasLasCajasAsync(ct);
        return await motor.EstadoAsync(ct);
    }

    public async Task<ResultadoConsulta> EjecutarAsync(int consulta, int? anio, int page, int pageSize, CancellationToken ct = default)
    {
        await ExigirTodasLasCajasAsync(ct);
        return await motor.EjecutarAsync(consulta, anio, page, pageSize, ct);
    }

    public async Task<(string Nombre, byte[] Contenido)> ExportarCsvAsync(int consulta, int? anio, CancellationToken ct = default)
    {
        await ExigirTodasLasCajasAsync(ct);
        return await motor.ExportarCsvAsync(consulta, anio, ct);
    }

    public async Task<EquivalenciaDto> VerificarEquivalenciaAsync(int consulta, int? anio, CancellationToken ct = default)
    {
        await ExigirTodasLasCajasAsync(ct);
        return await motor.VerificarEquivalenciaAsync(consulta, anio, ct);
    }

    private async Task ExigirTodasLasCajasAsync(CancellationToken ct)
    {
        var permitidas = await acceso.CajasPermitidasAsync(ct);
        var todas = await db.Cajas.AsNoTracking().Select(c => c.Id).ToListAsync(ct);
        if (todas.Any(id => !permitidas.Contains(id)))
            throw ErrorNegocio.Prohibido(Codigos.ConsultaRequiereTodasLasCajas,
                "Las consultas distribuidas abarcan todas las cajas y tu usuario no tiene acceso a todas.");
    }
}

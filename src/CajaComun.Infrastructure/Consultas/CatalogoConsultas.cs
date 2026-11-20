using System.Globalization;
using System.Text.RegularExpressions;
using CajaComun.Application.Consultas;

namespace CajaComun.Infrastructure.Consultas;

/// <summary>
/// Las cinco consultas de la Parte 2. El SQL no está duplicado en el código: se lee de los mismos archivos que se
/// entregan en docs/ (embebidos en el ensamblado), así lo que se ejecuta y lo que se documenta no pueden diferir.
/// </summary>
public static class CatalogoConsultas
{
    public const string ArchivoFederado = "consultas-parte2.sql";
    public const string ArchivoDirecto = "consultas-parte2-mariadb.sql";

    public static readonly IReadOnlyList<ConsultaDefinicion> Definiciones =
    [
        new(1, UsaAnio: false, UsaFechaCorte: false),
        new(2, UsaAnio: false, UsaFechaCorte: false),
        new(3, UsaAnio: false, UsaFechaCorte: true),
        new(4, UsaAnio: true, UsaFechaCorte: false),
        new(5, UsaAnio: true, UsaFechaCorte: false),
    ];

    private static readonly Lazy<IReadOnlyDictionary<int, string>> Federadas = new(() => Cargar(ArchivoFederado));
    private static readonly Lazy<IReadOnlyDictionary<int, string>> Directas = new(() => Cargar(ArchivoDirecto));

    public static ConsultaDefinicion? Buscar(int numero) => Definiciones.FirstOrDefault(d => d.Numero == numero);

    /// <summary>SQL de Trino con los parámetros ya sustituidos.</summary>
    public static string Federada(ConsultaDefinicion consulta, int? anio, DateOnly corte) =>
        Preparar(Federadas.Value[consulta.Numero], anio, corte);

    /// <summary>SQL equivalente sobre MariaDB, con los mismos parámetros.</summary>
    public static string Directa(ConsultaDefinicion consulta, int? anio, DateOnly corte) =>
        Preparar(Directas.Value[consulta.Numero], anio, corte);

    /// <summary>
    /// Solo se sustituyen enteros y una fecha generada aquí (nunca texto del usuario), así que no hay forma de
    /// inyectar SQL por los parámetros.
    /// </summary>
    internal static string Preparar(string sql, int? anio, DateOnly corte)
    {
        var resultado = sql.Replace("{{fecha_corte}}", $"DATE '{corte.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}'");
        if (anio is { } a)
        {
            resultado = resultado
                .Replace("{{anio}}", a.ToString(CultureInfo.InvariantCulture))
                .Replace("{{anio_sig}}", (a + 1).ToString(CultureInfo.InvariantCulture));
        }
        return resultado;
    }

    internal static IReadOnlyDictionary<int, string> Dividir(string texto)
    {
        var bloques = new Dictionary<int, string>();
        var partes = MarcaConsulta.Split(texto);
        // Con un grupo de captura, Split intercala los números: [encabezado, "1", cuerpo1, "2", cuerpo2, ...]
        for (var i = 1; i + 1 < partes.Length; i += 2)
            bloques[int.Parse(partes[i], CultureInfo.InvariantCulture)] = partes[i + 1].Trim().TrimEnd(';').Trim();
        return bloques;
    }

    private static Dictionary<int, string> Cargar(string recurso)
    {
        var asm = typeof(CatalogoConsultas).Assembly;
        using var flujo = asm.GetManifestResourceStream($"consultas/{recurso}")
            ?? throw new InvalidOperationException($"Falta el recurso embebido consultas/{recurso}.");
        using var lector = new StreamReader(flujo);
        var bloques = Dividir(lector.ReadToEnd());
        foreach (var d in Definiciones)
            if (!bloques.ContainsKey(d.Numero))
                throw new InvalidOperationException($"{recurso} no trae la consulta {d.Numero}.");
        return new Dictionary<int, string>(bloques);
    }

    private static readonly Regex MarcaConsulta = new(@"^-- @consulta (\d+)\s*$", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Para las pruebas: lee un recurso de consultas sin pasar por la caché.</summary>
    internal static string Recurso(string nombre)
    {
        using var flujo = typeof(CatalogoConsultas).Assembly.GetManifestResourceStream($"consultas/{nombre}")!;
        using var lector = new StreamReader(flujo);
        return lector.ReadToEnd();
    }
}

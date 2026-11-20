using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CajaComun.Application.Ciclos;
using CajaComun.Application.Comun;
using CajaComun.Application.Nomina;
using CajaComun.Domain.Reglas;

namespace CajaComun.Infrastructure.Archivos;

/// <summary>Texto permitido en los archivos bancarios: mayúsculas sin acentos y un conjunto corto de signos (sin coma, que separa los campos del CSV).</summary>
public static class TextoBancario
{
    private static readonly Regex NoPermitidos = new("[^A-Z0-9 .&-]", RegexOptions.Compiled);
    private static readonly Regex Espacios = new(" {2,}", RegexOptions.Compiled);

    public static string Normalizar(string? texto, int maximo)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        var sinAcentos = new StringBuilder();
        foreach (var c in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sinAcentos.Append(c);
        var limpio = NoPermitidos.Replace(sinAcentos.ToString().ToUpperInvariant(), " ");
        limpio = Espacios.Replace(limpio, " ").Trim();
        return limpio.Length <= maximo ? limpio : limpio[..maximo].TrimEnd();
    }
}

/// <summary>Validaciones y utilidades comunes de los layouts de dispersión.</summary>
internal static class DispersionComun
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Importe(decimal importe) => importe.ToString("0.00", Inv);

    public static string Concepto(int anio, bool incluirAhorro, int maximo) =>
        TextoBancario.Normalizar(incluirAhorro ? $"AHORRO Y RENDIMIENTO {anio}" : $"RENDIMIENTO {anio}", maximo);

    /// <summary>Referencia numérica de 10 dígitos: año y consecutivo de 6 dígitos del renglón.</summary>
    public static string Referencia(int anio, int consecutivo) => $"{anio:0000}{consecutivo:000000}";

    /// <summary>Revisa todos los renglones y, si alguno falla, informa cuáles en lugar de omitirlos.</summary>
    public static void Validar(IReadOnlyList<DispersionSpei> dispersiones, int maximoNombre)
    {
        if (dispersiones.Count == 0)
            throw ErrorNegocio.Conflicto(Codigos.SpeiSinDispersiones, "El ciclo no tiene importes que dispersar.");
        if (dispersiones.Count >= 1_000_000)
            throw new ErrorNegocio(Codigos.SpeiRenglonesInvalidos, "El archivo excede un millón de renglones.", 400);

        var errores = new List<string>();
        foreach (var d in dispersiones)
        {
            if (!Clabe.EsValida(d.Clabe)) errores.Add($"{d.NumEmpleado}:CLABE_INVALIDA");
            if (d.Importe <= 0m || decimal.Round(d.Importe, 2) != d.Importe) errores.Add($"{d.NumEmpleado}:MONTO_INVALIDO");
            if (TextoBancario.Normalizar(d.Nombre, maximoNombre).Length == 0) errores.Add($"{d.NumEmpleado}:NOMBRE_INVALIDO");
        }
        if (errores.Count > 0)
            throw new ErrorNegocio(Codigos.SpeiRenglonesInvalidos,
                $"{errores.Count} validaciones fallaron en el archivo de dispersión.", 400,
                new Dictionary<string, string[]> { ["renglones"] = errores.Take(50).ToArray() });
    }

    /// <summary>SHA-256 de los renglones canónicos (clabe|importe|referencia) en el orden del archivo.</summary>
    public static string SumaControl(IEnumerable<string> renglones) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', renglones)))).ToLowerInvariant();

    public static byte[] Texto(string contenido) => new UTF8Encoding(false).GetBytes(contenido);
}

/// <summary>Layout propio en CSV: sirve para revisar el archivo y como base de cualquier otro formato.</summary>
public sealed class LayoutSpeiGenerico : ILayoutBancario
{
    public FormatoSpei Formato => FormatoSpei.GenericoCsv;

    public ArchivoGenerado Generar(
        IReadOnlyList<DispersionSpei> dispersiones, int anio, DateOnly fechaAplicacion, bool incluirAhorro, string claveCaja)
    {
        DispersionComun.Validar(dispersiones, 40);
        var concepto = DispersionComun.Concepto(anio, incluirAhorro, 30);
        var sb = new StringBuilder("clabe,beneficiario,importe,concepto,referencia,fecha_aplicacion\r\n");
        var canonicos = new List<string>(dispersiones.Count);
        for (var i = 0; i < dispersiones.Count; i++)
        {
            var d = dispersiones[i];
            var referencia = DispersionComun.Referencia(anio, i + 1);
            var importe = DispersionComun.Importe(d.Importe);
            sb.Append($"{d.Clabe},{TextoBancario.Normalizar(d.Nombre, 40)},{importe},{concepto},{referencia},{fechaAplicacion:yyyy-MM-dd}\r\n");
            canonicos.Add($"{d.Clabe}|{importe}|{referencia}");
        }
        return new ArchivoGenerado(
            DispersionComun.Texto(sb.ToString()), $"SPEI-{claveCaja}-{anio}.csv", "text/csv; charset=utf-8",
            dispersiones.Count, dispersiones.Sum(d => d.Importe), DispersionComun.SumaControl(canonicos));
    }
}

/// <summary>
/// Texto delimitado por tabuladores, sin encabezado, según el layout simplificado de pagos de Banorte. Las longitudes de
/// cuenta (20), referencia (10) y descripción (30) y el formato del importe están verificadas; el orden de las columnas
/// y el ID del beneficiario son supuestos (ver docs/layout-spei.md).
/// </summary>
public sealed class LayoutSpeiBanorteSimplificado : ILayoutBancario
{
    public FormatoSpei Formato => FormatoSpei.BanorteSimplificado;

    public ArchivoGenerado Generar(
        IReadOnlyList<DispersionSpei> dispersiones, int anio, DateOnly fechaAplicacion, bool incluirAhorro, string claveCaja)
    {
        DispersionComun.Validar(dispersiones, 40);
        var descripcion = DispersionComun.Concepto(anio, incluirAhorro, 30);
        var sb = new StringBuilder();
        var canonicos = new List<string>(dispersiones.Count);
        for (var i = 0; i < dispersiones.Count; i++)
        {
            var d = dispersiones[i];
            var referencia = DispersionComun.Referencia(anio, i + 1);
            var importe = DispersionComun.Importe(d.Importe);
            sb.Append($"{d.NumEmpleado}\t{d.Clabe.PadLeft(20, '0')}\t{importe}\t{referencia}\t{descripcion}\r\n");
            canonicos.Add($"{d.Clabe}|{importe}|{referencia}");
        }
        return new ArchivoGenerado(
            DispersionComun.Texto(sb.ToString()), $"SPEI-{claveCaja}-{anio}-BANORTE.txt", "text/plain; charset=utf-8",
            dispersiones.Count, dispersiones.Sum(d => d.Importe), DispersionComun.SumaControl(canonicos));
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CajaComun.Application.Nomina;
using CajaComun.Infrastructure.Persistence;
using ClosedXML.Excel;

namespace CajaComun.Infrastructure.Archivos;

public sealed record LineaLayout(string NumEmpleado, string Concepto, decimal Importe);

/// <summary>
/// Layout de incidencias para el sistema de nómina: un renglón por empleado y concepto, con el voluntario aparte,
/// y un cierre con el conteo y el importe. Formato descrito en docs/layout-nomina.md.
/// </summary>
public static class LayoutNomina
{
    public const string ConceptoOrdinario = "AHO-ORD";
    public const string ConceptoVoluntario = "AHO-VOL";
    public const string ConceptoPrestamo = "PREST";

    private const int AnchoRegistro = 44;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private static readonly string[] Encabezado = ["num_empleado", "clave_concepto", "importe", "periodo", "tipo_periodo"];

    public static string Extension(FormatoLayout formato) => formato switch
    {
        FormatoLayout.Csv => "csv",
        FormatoLayout.Xlsx => "xlsx",
        _ => "txt"
    };

    public static string TipoContenido(FormatoLayout formato) => formato switch
    {
        FormatoLayout.Csv => "text/csv; charset=utf-8",
        FormatoLayout.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "text/plain; charset=utf-8"
    };

    public static string NombreFormato(FormatoLayout formato) => ConversionesEnum.ADb(formato);

    /// <summary>
    /// Suma de control del contenido, igual en todos los formatos: SHA-256 de los renglones en su forma canónica
    /// (empleado|concepto|importe|periodo|tipo), en el orden del archivo.
    /// </summary>
    public static string SumaControl(IEnumerable<LineaLayout> lineas, string periodo, string tipoPeriodo)
    {
        var texto = string.Join('\n', lineas.Select(l => Canonica(l, periodo, tipoPeriodo)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(texto))).ToLowerInvariant();
    }

    public static byte[] Generar(
        FormatoLayout formato, IReadOnlyList<LineaLayout> lineas, string claveCaja, string periodo, string tipoPeriodo)
    {
        var total = lineas.Sum(l => l.Importe);
        return formato switch
        {
            FormatoLayout.Csv => Texto(Csv(lineas, periodo, tipoPeriodo, total)),
            FormatoLayout.TxtPipe => Texto(Pipe(lineas, claveCaja, periodo, tipoPeriodo, total)),
            FormatoLayout.TxtAncho => Texto(Ancho(lineas, periodo, tipoPeriodo, total)),
            FormatoLayout.Xlsx => Libro(lineas, periodo, tipoPeriodo, total),
            _ => throw new ArgumentOutOfRangeException(nameof(formato))
        };
    }

    private static string Importe(decimal importe) => importe.ToString("0.00", Inv);

    private static string Canonica(LineaLayout l, string periodo, string tipo) =>
        $"{l.NumEmpleado}|{l.Concepto}|{Importe(l.Importe)}|{periodo}|{tipo}";

    private static byte[] Texto(string contenido) => new UTF8Encoding(false).GetBytes(contenido);

    private static string Csv(IReadOnlyList<LineaLayout> lineas, string periodo, string tipo, decimal total)
    {
        var sb = new StringBuilder();
        sb.Append(string.Join(',', Encabezado)).Append("\r\n");
        foreach (var l in lineas)
            sb.Append($"{l.NumEmpleado},{l.Concepto},{Importe(l.Importe)},{periodo},{tipo}\r\n");
        // Cierre: la segunda columna lleva el número de renglones y la tercera el importe total.
        sb.Append($"TOTAL,{lineas.Count},{Importe(total)},{periodo},{tipo}\r\n");
        return sb.ToString();
    }

    private static string Pipe(IReadOnlyList<LineaLayout> lineas, string clave, string periodo, string tipo, decimal total)
    {
        var sb = new StringBuilder();
        sb.Append($"H|NOMINA|{clave}|{periodo}|{tipo}\r\n");
        foreach (var l in lineas) sb.Append($"D|{Canonica(l, periodo, tipo)}\r\n");
        sb.Append($"T|{lineas.Count}|{Importe(total)}\r\n");
        return sb.ToString();
    }

    /// <summary>
    /// Ancho fijo de 44 posiciones: tipo de registro (1), empleado (12, a la izquierda), concepto (8), importe en
    /// centavos con ceros a la izquierda (12), periodo (10) y tipo de periodo (1, Q o S).
    /// El encabezado y el cierre se rellenan con espacios al mismo ancho.
    /// </summary>
    private static string Ancho(IReadOnlyList<LineaLayout> lineas, string periodo, string tipo, decimal total)
    {
        var sb = new StringBuilder();
        sb.Append(Relleno($"H{"NOMINA",-12}{"",-8}{0L,12:000000000000}{periodo,-10}{tipo[0]}")).Append("\r\n");
        foreach (var l in lineas)
            sb.Append($"D{l.NumEmpleado,-12}{l.Concepto,-8}{Centavos(l.Importe):000000000000}{periodo,-10}{tipo[0]}").Append("\r\n");
        sb.Append(Relleno($"T{lineas.Count,-12:00000000}{"",-8}{Centavos(total):000000000000}{periodo,-10}{tipo[0]}")).Append("\r\n");
        return sb.ToString();
    }

    private static string Relleno(string registro) => registro.PadRight(AnchoRegistro);

    private static long Centavos(decimal importe) => (long)decimal.Round(importe * 100m, 0, MidpointRounding.AwayFromZero);

    private static byte[] Libro(IReadOnlyList<LineaLayout> lineas, string periodo, string tipo, decimal total)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet("Incidencias");
        for (var c = 0; c < Encabezado.Length; c++) hoja.Cell(1, c + 1).Value = Encabezado[c];
        hoja.Row(1).Style.Font.Bold = true;

        var fila = 2;
        foreach (var l in lineas)
        {
            hoja.Cell(fila, 1).Value = l.NumEmpleado;
            hoja.Cell(fila, 2).Value = l.Concepto;
            hoja.Cell(fila, 3).Value = l.Importe;
            hoja.Cell(fila, 4).Value = periodo;
            hoja.Cell(fila, 5).Value = tipo;
            fila++;
        }
        hoja.Cell(fila, 1).Value = "TOTAL";
        hoja.Cell(fila, 2).Value = lineas.Count;
        hoja.Cell(fila, 3).Value = total;
        hoja.Cell(fila, 4).Value = periodo;
        hoja.Cell(fila, 5).Value = tipo;
        hoja.Row(fila).Style.Font.Bold = true;
        hoja.Range(2, 3, fila, 3).Style.NumberFormat.Format = "0.00";
        hoja.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        libro.SaveAs(ms);
        return ms.ToArray();
    }
}

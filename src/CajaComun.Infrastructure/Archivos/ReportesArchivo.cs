using System.Globalization;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace CajaComun.Infrastructure.Archivos;

public enum TipoColumna { Texto, Dinero, Entero, Fecha, Porcentaje, Decimal }

public sealed record ColumnaReporte(string Titulo, TipoColumna Tipo, float Peso = 1f);

/// <summary>Un reporte tabular independiente del formato: lo consumen por igual el generador de PDF y el de Excel.</summary>
public sealed record TablaReporte(
    string Titulo, string Subtitulo, IReadOnlyList<(string Etiqueta, string Valor)> Resumen,
    IReadOnlyList<ColumnaReporte> Columnas, IReadOnlyList<object?[]> Filas, object?[]? Totales = null);

public static class ReportePdf
{
    private const string Rosa = "#E91E63";
    private const string Vino = "#880E4F";
    private const string Tinte = "#FCE4EC";
    private static int _licencia;

    public static string Formatear(object? valor, TipoColumna tipo) => valor switch
    {
        null => "",
        decimal d when tipo == TipoColumna.Dinero => "$" + d.ToString("#,##0.00", CultureInfo.InvariantCulture),
        decimal d when tipo == TipoColumna.Porcentaje => (d * 100m).ToString("0.0000", CultureInfo.InvariantCulture) + " %",
        decimal d => d.ToString("#,##0.00", CultureInfo.InvariantCulture),
        DateOnly f => f.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture),
        DateTime f => f.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
        int i => i.ToString("#,##0", CultureInfo.InvariantCulture),
        long l => l.ToString("#,##0", CultureInfo.InvariantCulture),
        _ => valor.ToString() ?? ""
    };

    public static byte[] Generar(TablaReporte tabla)
    {
        // La licencia Community de QuestPDF cubre proyectos de uso académico y organizaciones pequeñas.
        if (Interlocked.Exchange(ref _licencia, 1) == 0) QuestPDF.Settings.License = LicenseType.Community;

        return Document.Create(doc => doc.Page(pagina =>
        {
            pagina.Size(PageSizes.Letter);
            pagina.Margin(32);
            pagina.DefaultTextStyle(t => t.FontSize(9));

            pagina.Header().Column(col =>
            {
                col.Item().Text("Caja Común").FontSize(10).FontColor(Rosa).Bold();
                col.Item().Text(tabla.Titulo).FontSize(16).FontColor(Vino).Bold();
                if (tabla.Subtitulo != "") col.Item().Text(tabla.Subtitulo).FontSize(9).FontColor(Colors.Grey.Darken1);
                col.Item().PaddingVertical(6).LineHorizontal(1).LineColor(Rosa);
            });

            pagina.Content().Column(col =>
            {
                if (tabla.Resumen.Count > 0)
                {
                    col.Item().PaddingBottom(8).Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(1); });
                        foreach (var (etiqueta, valor) in tabla.Resumen)
                        {
                            t.Cell().PaddingVertical(2).Text(etiqueta).FontColor(Colors.Grey.Darken2);
                            t.Cell().PaddingVertical(2).AlignRight().Text(valor).Bold().FontColor(Vino);
                        }
                    });
                }

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(c => { foreach (var k in tabla.Columnas) c.RelativeColumn(k.Peso); });
                    t.Header(h =>
                    {
                        foreach (var k in tabla.Columnas)
                            h.Cell().Background(Tinte).Padding(4).Text(k.Titulo).Bold().FontColor(Vino).FontSize(8);
                    });
                    foreach (var fila in tabla.Filas) Renglon(t, tabla.Columnas, fila, negrita: false);
                    if (tabla.Totales is not null) Renglon(t, tabla.Columnas, tabla.Totales, negrita: true);
                });
            });

            pagina.Footer().AlignCenter().Text(x =>
            {
                x.Span("Página ").FontSize(8);
                x.CurrentPageNumber().FontSize(8);
                x.Span(" de ").FontSize(8);
                x.TotalPages().FontSize(8);
            });
        })).GeneratePdf();
    }

    private static void Renglon(TableDescriptor t, IReadOnlyList<ColumnaReporte> columnas, object?[] fila, bool negrita)
    {
        for (var i = 0; i < columnas.Count; i++)
        {
            var celda = t.Cell().BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(3);
            var numerica = columnas[i].Tipo is TipoColumna.Dinero or TipoColumna.Entero or TipoColumna.Porcentaje or TipoColumna.Decimal;
            var texto = (numerica ? celda.AlignRight() : celda).Text(Formatear(fila[i], columnas[i].Tipo)).FontSize(8);
            if (negrita) texto.Bold();
        }
    }
}

public static class ReporteXlsx
{
    public static byte[] Generar(TablaReporte tabla)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet(NombreHoja(tabla.Titulo));
        var fila = 1;
        hoja.Cell(fila, 1).Value = tabla.Titulo;
        hoja.Cell(fila, 1).Style.Font.Bold = true;
        hoja.Cell(fila, 1).Style.Font.FontSize = 14;
        fila++;
        if (tabla.Subtitulo != "") hoja.Cell(fila++, 1).Value = tabla.Subtitulo;
        foreach (var (etiqueta, valor) in tabla.Resumen)
        {
            hoja.Cell(fila, 1).Value = etiqueta;
            hoja.Cell(fila, 2).Value = valor;
            fila++;
        }
        fila++;

        var encabezado = fila;
        for (var c = 0; c < tabla.Columnas.Count; c++)
        {
            var celda = hoja.Cell(fila, c + 1);
            celda.Value = tabla.Columnas[c].Titulo;
            celda.Style.Font.Bold = true;
            celda.Style.Fill.BackgroundColor = XLColor.FromHtml("#FCE4EC");
        }
        fila++;

        foreach (var datos in tabla.Filas) Escribir(hoja, tabla.Columnas, datos, fila++, negrita: false);
        if (tabla.Totales is not null) Escribir(hoja, tabla.Columnas, tabla.Totales, fila++, negrita: true);

        hoja.SheetView.FreezeRows(encabezado);
        hoja.Columns().AdjustToContents(1, 60);
        using var ms = new MemoryStream();
        libro.SaveAs(ms);
        return ms.ToArray();
    }

    private static void Escribir(IXLWorksheet hoja, IReadOnlyList<ColumnaReporte> columnas, object?[] datos, int fila, bool negrita)
    {
        for (var c = 0; c < columnas.Count; c++)
        {
            var celda = hoja.Cell(fila, c + 1);
            switch (datos[c])
            {
                case null: break;
                case decimal d: celda.Value = d; break;
                case int i: celda.Value = i; break;
                case long l: celda.Value = l; break;
                case DateOnly f: celda.Value = f.ToDateTime(TimeOnly.MinValue); celda.Style.DateFormat.Format = "dd/mm/yyyy"; break;
                case DateTime f: celda.Value = f; celda.Style.DateFormat.Format = "dd/mm/yyyy hh:mm"; break;
                default: celda.Value = datos[c]!.ToString(); break;
            }
            celda.Style.NumberFormat.Format = columnas[c].Tipo switch
            {
                TipoColumna.Dinero => "$#,##0.00",
                TipoColumna.Porcentaje => "0.0000%",
                TipoColumna.Decimal => "#,##0.00",
                TipoColumna.Entero => "#,##0",
                _ => celda.Style.NumberFormat.Format
            };
            if (negrita) celda.Style.Font.Bold = true;
        }
    }

    private static string NombreHoja(string titulo)
    {
        var limpio = new string(titulo.Where(c => !"[]:*?/\\".Contains(c)).ToArray());
        return limpio.Length <= 31 ? limpio : limpio[..31];
    }
}

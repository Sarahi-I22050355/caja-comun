using System.IO.Compression;
using System.Text;
using CajaComun.Application.Nomina;
using CajaComun.Infrastructure.Archivos;
using ClosedXML.Excel;

namespace CajaComun.Tests;

public class LayoutNominaTests
{
    private static readonly LineaLayout[] Lineas =
    [
        new("GIM-00123", LayoutNomina.ConceptoOrdinario, 1_400.00m),
        new("GIM-00123", LayoutNomina.ConceptoVoluntario, 500.00m),
        new("GIM-00123", LayoutNomina.ConceptoPrestamo, 1_362.50m),
        new("GIM-00124", LayoutNomina.ConceptoOrdinario, 900m),
    ];

    private static string[] Renglones(FormatoLayout formato) =>
        Encoding.UTF8.GetString(LayoutNomina.Generar(formato, Lineas, "GIM", "Q21-2024", "QUINCENAL"))
            .Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    [Fact]
    public void El_csv_trae_encabezado_un_renglon_por_concepto_y_cierre_con_conteo_e_importe()
    {
        var r = Renglones(FormatoLayout.Csv);
        Assert.Equal("num_empleado,clave_concepto,importe,periodo,tipo_periodo", r[0]);
        Assert.Equal("GIM-00123,AHO-ORD,1400.00,Q21-2024,QUINCENAL", r[1]);
        Assert.Equal("GIM-00123,AHO-VOL,500.00,Q21-2024,QUINCENAL", r[2]);
        Assert.Equal("GIM-00123,PREST,1362.50,Q21-2024,QUINCENAL", r[3]);
        Assert.Equal("TOTAL,4,4162.50,Q21-2024,QUINCENAL", r[^1]);
        Assert.Equal(6, r.Length);
    }

    [Fact]
    public void El_importe_usa_punto_decimal_sin_depender_de_la_cultura()
    {
        var anterior = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("es-MX");
            Assert.Contains("1400.00", Renglones(FormatoLayout.Csv)[1]);
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Contains("1400.00", Renglones(FormatoLayout.TxtPipe)[1]);
        }
        finally { Thread.CurrentThread.CurrentCulture = anterior; }
    }

    [Fact]
    public void El_txt_con_pipes_lleva_encabezado_detalle_y_cierre()
    {
        var r = Renglones(FormatoLayout.TxtPipe);
        Assert.Equal("H|NOMINA|GIM|Q21-2024|QUINCENAL", r[0]);
        Assert.Equal("D|GIM-00123|AHO-ORD|1400.00|Q21-2024|QUINCENAL", r[1]);
        Assert.Equal("T|4|4162.50", r[^1]);
    }

    [Fact]
    public void El_txt_de_ancho_fijo_mide_44_posiciones_con_el_importe_en_centavos()
    {
        var r = Renglones(FormatoLayout.TxtAncho);
        Assert.All(r, l => Assert.Equal(44, l.Length));
        Assert.Equal("DGIM-00123   AHO-ORD 000000140000Q21-2024  Q", r[1]);
        Assert.Equal("DGIM-00123   PREST   000000136250Q21-2024  Q", r[3]);
        Assert.StartsWith("T00000004", r[^1]);
        Assert.Contains("000000416250", r[^1]);
        Assert.StartsWith("HNOMINA", r[0]);
    }

    [Fact]
    public void El_libro_de_excel_trae_importes_numericos_y_el_cierre()
    {
        var bytes = LayoutNomina.Generar(FormatoLayout.Xlsx, Lineas, "GIM", "Q21-2024", "QUINCENAL");
        Assert.Contains("xl/workbook.xml", new ZipArchive(new MemoryStream(bytes)).Entries.Select(e => e.FullName));

        using var libro = new XLWorkbook(new MemoryStream(bytes));
        var hoja = libro.Worksheet("Incidencias");
        Assert.Equal("clave_concepto", hoja.Cell(1, 2).GetString());
        Assert.True(hoja.Cell(4, 3).Value.IsNumber);
        Assert.Equal(1362.5, hoja.Cell(4, 3).Value.GetNumber());
        Assert.Equal("TOTAL", hoja.Cell(6, 1).GetString());
        Assert.Equal(4, hoja.Cell(6, 2).Value.GetNumber());
        Assert.Equal(4162.5, hoja.Cell(6, 3).Value.GetNumber());
    }

    [Fact]
    public void La_suma_de_control_es_estable_y_cambia_con_cualquier_dato()
    {
        var control = LayoutNomina.SumaControl(Lineas, "Q21-2024", "QUINCENAL");
        Assert.Equal(64, control.Length);
        Assert.Equal(control, LayoutNomina.SumaControl(Lineas, "Q21-2024", "QUINCENAL"));

        var distinto = Lineas.ToArray();
        distinto[3] = distinto[3] with { Importe = 900.01m };
        Assert.NotEqual(control, LayoutNomina.SumaControl(distinto, "Q21-2024", "QUINCENAL"));
        Assert.NotEqual(control, LayoutNomina.SumaControl(Lineas, "Q22-2024", "QUINCENAL"));
        Assert.NotEqual(control, LayoutNomina.SumaControl(Lineas.AsEnumerable().Reverse(), "Q21-2024", "QUINCENAL"));
    }

    [Theory]
    [InlineData(FormatoLayout.Csv, "csv")]
    [InlineData(FormatoLayout.Xlsx, "xlsx")]
    [InlineData(FormatoLayout.TxtPipe, "txt")]
    [InlineData(FormatoLayout.TxtAncho, "txt")]
    public void Cada_formato_tiene_su_extension(FormatoLayout formato, string extension) =>
        Assert.Equal(extension, LayoutNomina.Extension(formato));

    [Fact]
    public void Los_nombres_de_formato_caben_en_la_columna_del_historial()
    {
        foreach (var f in Enum.GetValues<FormatoLayout>())
            Assert.InRange(LayoutNomina.NombreFormato(f).Length, 1, 10);
        Assert.Equal("TXT_PIPE", LayoutNomina.NombreFormato(FormatoLayout.TxtPipe));
    }
}

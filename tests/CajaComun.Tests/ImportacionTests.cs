using System.Text;
using CajaComun.Application.Comun;
using CajaComun.Application.Importaciones;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;
using CajaComun.Infrastructure.Archivos;
using ClosedXML.Excel;

namespace CajaComun.Tests;

public class ValidadorFilaImportacionTests
{
    private static readonly DateOnly Hoy = new(2024, 11, 1);

    private static Dictionary<string, string?> AltaValida() => new()
    {
        ["movimiento"] = "ALTA", ["num_empleado"] = "GIM-09901", ["nombre"] = "Julia", ["apellidos"] = "Pérez",
        ["rfc"] = "PEMJ900517AB1", ["fecha_nacimiento"] = "1990-05-17", ["fecha_ingreso"] = "2024-10-01",
        ["sueldo_quincenal"] = "9000.00", ["clabe"] = Clabe.Completar("01218000123456789"), ["departamento"] = "Almacén",
        ["cuota_ordinaria"] = "450.00", ["cuota_voluntaria"] = "", ["voluntario_activo"] = ""
    };

    private static string[] Codigos(FilaValidada f) => f.Errores.Select(e => $"{e.Campo}:{e.Codigo}").ToArray();

    [Fact]
    public void Un_alta_completa_se_valida_y_conserva_los_datos()
    {
        var r = ValidadorFilaImportacion.Validar(AltaValida(), "GIM", Hoy);
        Assert.Empty(r.Errores);
        Assert.Equal(TipoCambioImportacion.Alta, r.Movimiento);
        Assert.Equal("Julia", r.Alta!.Nombre);
        Assert.Equal(450m, r.Alta.CuotaOrdinaria);
        Assert.Equal(new DateOnly(2024, 10, 1), r.Alta.FechaIngreso);
        Assert.False(r.Alta.VoluntarioActivo);
    }

    [Fact]
    public void El_voluntario_se_activa_solo_si_trae_importe_y_no_se_acepta_importe_inactivo()
    {
        var conVoluntario = AltaValida();
        conVoluntario["cuota_voluntaria"] = "200";
        var r = ValidadorFilaImportacion.Validar(conVoluntario, "GIM", Hoy);
        Assert.True(r.Alta!.VoluntarioActivo);
        Assert.Equal(200m, r.Alta.CuotaVoluntaria);

        conVoluntario["voluntario_activo"] = "NO";
        Assert.Contains("cuota_voluntaria:VOLUNTARIO_INACTIVO", Codigos(ValidadorFilaImportacion.Validar(conVoluntario, "GIM", Hoy)));
    }

    [Fact]
    public void Cada_campo_invalido_reporta_su_propio_codigo()
    {
        var fila = AltaValida();
        fila["rfc"] = "MAL";
        fila["clabe"] = "012180001234567891";
        fila["fecha_ingreso"] = "31/02/2024";
        fila["sueldo_quincenal"] = "mucho";
        fila["cuota_ordinaria"] = "-5";
        fila["nombre"] = "";
        var codigos = Codigos(ValidadorFilaImportacion.Validar(fila, "GIM", Hoy));
        Assert.Contains("rfc:RFC_INVALIDO", codigos);
        Assert.Contains("clabe:CLABE_INVALIDA", codigos);
        Assert.Contains("fecha_ingreso:FECHA_INVALIDA", codigos);
        Assert.Contains("sueldo_quincenal:MONTO_INVALIDO", codigos);
        Assert.Contains("cuota_ordinaria:MONTO_NEGATIVO", codigos);
        Assert.Contains("nombre:REQUERIDO", codigos);
    }

    [Theory]
    [InlineData("1990-05-17", "2024-10-01", true)]
    [InlineData("17/05/1990", "01/10/2024", true)]
    [InlineData("2030-01-01", "2031-01-01", false)]      // nace en el futuro
    [InlineData("1990-05-17", "1989-01-01", false)]      // ingreso anterior al nacimiento
    [InlineData("1890-01-01", "2000-01-01", false)]
    public void Las_fechas_aceptan_dos_formatos_y_se_revisan_entre_si(string nacimiento, string ingreso, bool valida)
    {
        var fila = AltaValida();
        fila["fecha_nacimiento"] = nacimiento;
        fila["fecha_ingreso"] = ingreso;
        Assert.Equal(valida, ValidadorFilaImportacion.Validar(fila, "GIM", Hoy).Errores.Count == 0);
    }

    [Theory]
    [InlineData("1,400.00", true)]
    [InlineData("$1400", true)]
    [InlineData("1400.123", false)]       // más de dos decimales
    [InlineData("1.400,00", false)]
    [InlineData("", false)]
    public void Los_importes_son_decimales_con_a_lo_mas_dos_decimales(string valor, bool valido)
    {
        var fila = AltaValida();
        fila["sueldo_quincenal"] = valor;
        Assert.Equal(valido, ValidadorFilaImportacion.Validar(fila, "GIM", Hoy).Errores.Count == 0);
    }

    [Theory]
    [InlineData("GIM-09901", true)]
    [InlineData("gim-09901", true)]      // se pasa a mayúsculas
    [InlineData("ACE-00001", false)]     // prefijo de otra caja
    [InlineData("GIM-9901", false)]
    [InlineData("", false)]
    public void El_numero_de_empleado_lleva_el_prefijo_de_la_caja(string numero, bool valido)
    {
        var fila = AltaValida();
        fila["num_empleado"] = numero;
        Assert.Equal(valido, ValidadorFilaImportacion.Validar(fila, "GIM", Hoy).Errores.Count == 0);
    }

    [Fact]
    public void La_baja_pide_numero_y_fecha()
    {
        var baja = new Dictionary<string, string?> { ["movimiento"] = "baja", ["num_empleado"] = "GIM-00123", ["fecha_baja"] = "2024-11-05" };
        var r = ValidadorFilaImportacion.Validar(baja, "GIM", Hoy);
        Assert.Empty(r.Errores);
        Assert.Equal(new DateOnly(2024, 11, 5), r.Baja!.FechaBaja);

        baja["fecha_baja"] = "";
        Assert.Contains("fecha_baja:REQUERIDO", Codigos(ValidadorFilaImportacion.Validar(baja, "GIM", Hoy)));
    }

    [Theory]
    [InlineData("", "REQUERIDO")]
    [InlineData("MODIFICACION", "MOVIMIENTO_INVALIDO")]
    public void El_movimiento_debe_ser_alta_o_baja(string movimiento, string codigo)
    {
        var fila = AltaValida();
        fila["movimiento"] = movimiento;
        Assert.Contains($"movimiento:{codigo}", Codigos(ValidadorFilaImportacion.Validar(fila, "GIM", Hoy)));
    }

    [Theory]
    [InlineData("NumEmpleado", "num_empleado")]
    [InlineData("Fecha Nacimiento", "fecha_nacimiento")]
    [InlineData("SueldoQuincenal", "sueldo_quincenal")]
    [InlineData("RFC", "rfc")]
    [InlineData("Número_Empleado", "num_empleado")]
    [InlineData("tipo", "movimiento")]
    [InlineData("Cuota-Ordinaria", "cuota_ordinaria")]
    public void Los_nombres_de_columna_se_normalizan(string original, string esperado) =>
        Assert.Equal(esperado, ValidadorFilaImportacion.NormalizarColumna(original));
}

public class LectorImportacionTests
{
    private static byte[] Bytes(string texto) => new UTF8Encoding(false).GetBytes(texto);

    private static ErrorNegocio Falla(string archivo, byte[] contenido) =>
        Assert.Throws<ErrorNegocio>(() => LectorImportacion.Leer(archivo, contenido));

    [Fact]
    public void Un_txt_con_pipes_se_lee_con_las_columnas_normalizadas()
    {
        var r = LectorImportacion.Leer("altas.txt", Bytes("Movimiento|NumEmpleado|Nombre\nALTA|GIM-00001|Ana\n\nBAJA|GIM-00002|\n"));
        Assert.Equal(FormatoArchivo.Txt, r.Formato);
        Assert.Equal(["movimiento", "num_empleado", "nombre"], r.Columnas);
        Assert.Equal(2, r.Filas.Count);
        Assert.Equal(2, r.Filas[0].Numero);
        Assert.Equal(4, r.Filas[1].Numero);   // el número de renglón cuenta las líneas en blanco
        Assert.Equal("Ana", r.Filas[0].Datos["nombre"]);
    }

    [Fact]
    public void Un_csv_respeta_comillas_y_comas_dentro_del_texto()
    {
        var r = LectorImportacion.Leer("altas.csv", Bytes("movimiento,num_empleado,departamento\nALTA,GIM-00001,\"Compras, Nacional\"\nALTA,GIM-00002,\"Dice \"\"hola\"\"\"\n"));
        Assert.Equal(FormatoArchivo.Csv, r.Formato);
        Assert.Equal("Compras, Nacional", r.Filas[0].Datos["departamento"]);
        Assert.Equal("Dice \"hola\"", r.Filas[1].Datos["departamento"]);
    }

    [Fact]
    public void Un_archivo_con_bom_y_otra_codificacion_se_decodifica()
    {
        var conBom = new UTF8Encoding(true).GetPreamble().Concat(Bytes("movimiento|num_empleado\nALTA|GIM-00001\n")).ToArray();
        Assert.Equal("movimiento", LectorImportacion.Leer("a.txt", conBom).Columnas[0]);

        var latin1 = Encoding.Latin1.GetBytes("movimiento|num_empleado|nombre\nALTA|GIM-00001|José Muñoz\n");
        Assert.Equal("José Muñoz", LectorImportacion.Leer("a.txt", latin1).Filas[0].Datos["nombre"]);
    }

    [Fact]
    public void Un_xml_toma_atributos_y_elementos_en_camelCase()
    {
        const string xml = "<altasBajas><empleado movimiento='ALTA'><numEmpleado>GIM-00001</numEmpleado><fechaNacimiento>1990-01-02</fechaNacimiento></empleado>" +
                           "<empleado movimiento='BAJA'><numEmpleado>GIM-00002</numEmpleado></empleado></altasBajas>";
        var r = LectorImportacion.Leer("lote.xml", Bytes(xml));
        Assert.Equal(FormatoArchivo.Xml, r.Formato);
        Assert.Equal(2, r.Filas.Count);
        Assert.Equal("ALTA", r.Filas[0].Datos["movimiento"]);
        Assert.Equal("1990-01-02", r.Filas[0].Datos["fecha_nacimiento"]);
        Assert.Null(r.Filas[1].Datos["fecha_nacimiento"]);      // columna que solo trae otro renglón
    }

    [Fact]
    public void Un_xml_con_definicion_de_tipo_se_rechaza()
    {
        const string xxe = "<?xml version='1.0'?><!DOCTYPE a [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><r><e><num_empleado>&x;</num_empleado></e></r>";
        Assert.Equal(Codigos.ArchivoInvalido, Falla("a.xml", Bytes(xxe)).Codigo);
        Assert.Equal(Codigos.ArchivoInvalido, Falla("a.xml", Bytes("<r><e>")).Codigo);
    }

    [Fact]
    public void Un_xlsx_se_lee_con_fechas_y_numeros_sin_formato_regional()
    {
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet("Altas");
        hoja.Cell(1, 1).Value = "movimiento";
        hoja.Cell(1, 2).Value = "num_empleado";
        hoja.Cell(1, 3).Value = "fecha_ingreso";
        hoja.Cell(1, 4).Value = "cuota_ordinaria";
        hoja.Cell(2, 1).Value = "ALTA";
        hoja.Cell(2, 2).Value = "GIM-00001";
        hoja.Cell(2, 3).Value = new DateTime(2024, 10, 1);
        hoja.Cell(2, 4).Value = 1400.5;
        hoja.Cell(3, 1).Value = "BAJA";
        hoja.Cell(3, 2).Value = "GIM-00002";
        using var ms = new MemoryStream();
        libro.SaveAs(ms);

        var r = LectorImportacion.Leer("altas.xlsx", ms.ToArray());
        Assert.Equal(FormatoArchivo.Xlsx, r.Formato);
        Assert.Equal(2, r.Filas.Count);
        Assert.Equal("2024-10-01", r.Filas[0].Datos["fecha_ingreso"]);
        Assert.Equal("1400.5", r.Filas[0].Datos["cuota_ordinaria"]);
        Assert.Equal("", r.Filas[1].Datos["fecha_ingreso"]);
    }

    [Fact]
    public void Un_xlsx_con_celdas_muy_lejanas_se_rechaza_en_lugar_de_recorrer_millones_de_celdas()
    {
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet("Altas");
        hoja.Cell(1, 1).Value = "movimiento";
        hoja.Cell(2, 1).Value = "ALTA";
        hoja.Cell(2, 16_000).Value = "lejos";
        using var ms = new MemoryStream();
        libro.SaveAs(ms);
        Assert.Equal(Codigos.ArchivoInvalido, Falla("a.xlsx", ms.ToArray()).Codigo);
    }

    [Fact]
    public void Un_xlsx_falso_o_dañado_se_rechaza()
    {
        Assert.Equal(Codigos.ArchivoInvalido, Falla("a.xlsx", Bytes("no es un zip")).Codigo);
        Assert.Equal(Codigos.ArchivoInvalido, Falla("a.xlsx", [0x50, 0x4B, 0x03, 0x04, 0x00, 0x01]).Codigo);
    }

    [Theory]
    [InlineData("virus.exe")]
    [InlineData("datos.json")]
    [InlineData("sin_extension")]
    public void Solo_se_aceptan_las_extensiones_permitidas(string nombre) =>
        Assert.Equal(Codigos.ArchivoInvalido, Falla(nombre, Bytes("a|b\n1|2\n")).Codigo);

    [Fact]
    public void Un_archivo_vacio_binario_o_demasiado_grande_se_rechaza()
    {
        Assert.Equal(Codigos.ArchivoInvalido, Falla("a.txt", []).Codigo);
        Assert.Equal(Codigos.ArchivoInvalido, Falla("a.txt", [0x61, 0x00, 0x62]).Codigo);
        var grande = Falla("a.txt", new byte[LimitesImportacion.TamanoMaximoBytes + 1]);
        Assert.Equal(Codigos.ArchivoDemasiadoGrande, grande.Codigo);
        Assert.Equal(413, grande.Estado);
    }

    [Fact]
    public void Sin_renglones_o_con_demasiados_se_rechaza()
    {
        Assert.Equal(Codigos.ArchivoSinFilas, Falla("a.txt", Bytes("movimiento|num_empleado\n")).Codigo);

        var sb = new StringBuilder("movimiento|num_empleado\n");
        for (var i = 0; i <= LimitesImportacion.FilasMaximas; i++) sb.Append("ALTA|GIM-").Append(i.ToString("00000")).Append('\n');
        Assert.Equal(Codigos.ArchivoDemasiadasFilas, Falla("a.txt", Bytes(sb.ToString())).Codigo);
    }

    [Fact]
    public void Un_encabezado_con_columnas_repetidas_se_rechaza() =>
        Assert.Equal(Codigos.ArchivoInvalido, Falla("a.txt", Bytes("nombre|Nombre\nA|B\n")).Codigo);

    [Fact]
    public void Los_valores_muy_largos_se_recortan_para_no_guardar_basura()
    {
        var r = LectorImportacion.Leer("a.txt", Bytes("num_empleado|nombre\nGIM-00001|" + new string('x', 5_000) + "\n"));
        Assert.Equal(300, r.Filas[0].Datos["nombre"]!.Length);
    }
}

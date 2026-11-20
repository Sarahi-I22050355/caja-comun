using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CajaComun.Application.Comun;
using CajaComun.Application.Importaciones;
using CajaComun.Domain.Enums;
using ClosedXML.Excel;

namespace CajaComun.Infrastructure.Archivos;

public sealed record FilaArchivo(int Numero, Dictionary<string, string?> Datos);

public sealed record ArchivoLeido(FormatoArchivo Formato, IReadOnlyList<string> Columnas, IReadOnlyList<FilaArchivo> Filas);

/// <summary>
/// Lee los archivos de altas y bajas (.xlsx, .xml, .txt o .csv) y devuelve los renglones con las columnas ya
/// normalizadas. Solo traduce el formato: la validación de los datos es de ValidadorFilaImportacion.
/// </summary>
public static class LectorImportacion
{
    private const long TamanoDescomprimidoMaximo = 40L * 1024 * 1024;
    private const int LongitudMaximaValor = 300;
    private const int ColumnasMaximas = 60;

    public static ArchivoLeido Leer(string nombreArchivo, byte[] contenido)
    {
        var extension = Path.GetExtension(nombreArchivo).ToLowerInvariant();
        if (!LimitesImportacion.Extensiones.Contains(extension))
            throw Invalido("Solo se admiten archivos .xlsx, .xml, .txt o .csv.");
        if (contenido.Length == 0) throw Invalido("El archivo está vacío.");
        if (contenido.Length > LimitesImportacion.TamanoMaximoBytes)
            throw new ErrorNegocio(Codigos.ArchivoDemasiadoGrande, "El archivo excede el tamaño máximo permitido.", 413);

        var leido = extension switch
        {
            ".xlsx" => LeerXlsx(contenido),
            ".xml" => LeerXml(contenido),
            _ => LeerTexto(contenido, extension == ".csv" ? FormatoArchivo.Csv : FormatoArchivo.Txt)
        };

        if (leido.Filas.Count == 0) throw new ErrorNegocio(Codigos.ArchivoSinFilas, "El archivo no trae renglones de datos.", 400);
        if (leido.Filas.Count > LimitesImportacion.FilasMaximas)
            throw new ErrorNegocio(Codigos.ArchivoDemasiadasFilas,
                $"El archivo excede el máximo de {LimitesImportacion.FilasMaximas} renglones.", 400);
        return leido;
    }

    private static ErrorNegocio Invalido(string mensaje) => new(Codigos.ArchivoInvalido, mensaje, 400);

    // ---------- Texto delimitado ----------

    private static ArchivoLeido LeerTexto(byte[] bytes, FormatoArchivo formato)
    {
        if (Array.IndexOf(bytes, (byte)0) >= 0) throw Invalido("El archivo no es texto.");
        var texto = Decodificar(bytes);
        var lineas = texto.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        var indiceEncabezado = lineas.FindIndex(l => !string.IsNullOrWhiteSpace(l));
        if (indiceEncabezado < 0) throw Invalido("El archivo está vacío.");
        var separador = lineas[indiceEncabezado].Contains('|') ? '|' : ',';

        var columnas = NormalizarEncabezado(Dividir(lineas[indiceEncabezado], separador));
        var filas = new List<FilaArchivo>();
        for (var i = indiceEncabezado + 1; i < lineas.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(lineas[i])) continue;
            filas.Add(Armar(i + 1, columnas, Dividir(lineas[i], separador)));
            if (filas.Count > LimitesImportacion.FilasMaximas) break;
        }
        return new ArchivoLeido(formato, columnas, filas);
    }

    private static string Decodificar(byte[] bytes)
    {
        try { return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿'); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(bytes); }
    }

    /// <summary>Divide un renglón respetando comillas dobles (CSV básico: "a,b" y "" dentro de comillas).</summary>
    internal static List<string> Dividir(string linea, char separador)
    {
        var campos = new List<string>();
        var actual = new StringBuilder();
        var entreComillas = false;
        for (var i = 0; i < linea.Length; i++)
        {
            var c = linea[i];
            if (entreComillas)
            {
                if (c == '"' && i + 1 < linea.Length && linea[i + 1] == '"') { actual.Append('"'); i++; }
                else if (c == '"') entreComillas = false;
                else actual.Append(c);
            }
            else if (c == '"') entreComillas = true;
            else if (c == separador) { campos.Add(actual.ToString()); actual.Clear(); }
            else actual.Append(c);
        }
        campos.Add(actual.ToString());
        return campos;
    }

    // ---------- XML ----------

    private static ArchivoLeido LeerXml(byte[] bytes)
    {
        XDocument doc;
        try
        {
            var ajustes = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = LimitesImportacion.TamanoMaximoBytes * 2L
            };
            using var lector = XmlReader.Create(new MemoryStream(bytes), ajustes);
            doc = XDocument.Load(lector);
        }
        catch (XmlException) { throw Invalido("El archivo XML no está bien formado."); }

        var columnas = new List<string>();
        var filas = new List<FilaArchivo>();
        var numero = 0;
        foreach (var elemento in doc.Root?.Elements() ?? [])
        {
            numero++;
            var datos = new Dictionary<string, string?>();
            foreach (var a in elemento.Attributes()) Agregar(datos, columnas, a.Name.LocalName, a.Value);
            foreach (var h in elemento.Elements()) Agregar(datos, columnas, h.Name.LocalName, h.Value);
            filas.Add(new FilaArchivo(numero, datos));
            if (filas.Count > LimitesImportacion.FilasMaximas) break;
        }
        foreach (var f in filas)
            foreach (var c in columnas) f.Datos.TryAdd(c, null);
        return new ArchivoLeido(FormatoArchivo.Xml, columnas, filas);
    }

    private static void Agregar(Dictionary<string, string?> datos, List<string> columnas, string nombre, string valor)
    {
        var columna = ValidadorFilaImportacion.NormalizarColumna(nombre);
        if (!columnas.Contains(columna)) columnas.Add(columna);
        datos[columna] = Recortar(valor);
    }

    // ---------- Excel ----------

    private static ArchivoLeido LeerXlsx(byte[] bytes)
    {
        // Un .xlsx es un zip: se descarta lo que no lo parece y lo que se expande de forma desproporcionada.
        if (bytes.Length < 4 || bytes[0] != 0x50 || bytes[1] != 0x4B) throw Invalido("El archivo no es un libro de Excel válido.");
        try
        {
            using (var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read))
                if (zip.Entries.Sum(e => e.Length) > TamanoDescomprimidoMaximo)
                    throw Invalido("El libro de Excel es demasiado grande.");

            using var libro = new XLWorkbook(new MemoryStream(bytes));
            var hoja = libro.Worksheets.FirstOrDefault(h => h.Visibility == XLWorksheetVisibility.Visible) ?? libro.Worksheet(1);
            var rango = hoja.RangeUsed();
            if (rango is null) throw Invalido("La hoja está vacía.");

            var primera = rango.FirstRow().RowNumber();
            var ultima = rango.LastRow().RowNumber();
            if (ultima - primera > LimitesImportacion.FilasMaximas)
                throw new ErrorNegocio(Codigos.ArchivoDemasiadasFilas,
                    $"El archivo excede el máximo de {LimitesImportacion.FilasMaximas} renglones.", 400);

            var primeraCol = rango.FirstColumn().ColumnNumber();
            var ultimaCol = rango.LastColumn().ColumnNumber();
            if (ultimaCol - primeraCol + 1 > ColumnasMaximas) throw Invalido("El libro trae demasiadas columnas.");
            var encabezado = Enumerable.Range(primeraCol, ultimaCol - primeraCol + 1)
                .Select(c => TextoCelda(hoja.Cell(primera, c))).ToList();
            var columnas = NormalizarEncabezado(encabezado);

            var filas = new List<FilaArchivo>();
            for (var r = primera + 1; r <= ultima; r++)
            {
                var valores = Enumerable.Range(primeraCol, ultimaCol - primeraCol + 1).Select(c => TextoCelda(hoja.Cell(r, c))).ToList();
                if (valores.All(string.IsNullOrWhiteSpace)) continue;
                filas.Add(Armar(r, columnas, valores));
            }
            return new ArchivoLeido(FormatoArchivo.Xlsx, columnas, filas);
        }
        catch (ErrorNegocio) { throw; }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or NullReferenceException
                                       or InvalidOperationException or FormatException)
        {
            throw Invalido("No se pudo leer el libro de Excel.");
        }
    }

    /// <summary>Las fechas de Excel viajan como AAAA-MM-DD y los números sin formato regional.</summary>
    private static string TextoCelda(IXLCell celda)
    {
        var v = celda.Value;
        if (v.IsBlank) return "";
        if (v.IsDateTime) return v.GetDateTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (v.IsNumber) return Convert.ToDecimal(v.GetNumber()).ToString(CultureInfo.InvariantCulture);
        if (v.IsBoolean) return v.GetBoolean() ? "SI" : "NO";
        return Recortar(v.ToString(CultureInfo.InvariantCulture));
    }

    // ---------- Comunes ----------

    private static List<string> NormalizarEncabezado(IEnumerable<string> encabezado)
    {
        var columnas = encabezado.Select(ValidadorFilaImportacion.NormalizarColumna).ToList();
        if (columnas.Where(c => c != "").GroupBy(c => c).Any(g => g.Count() > 1))
            throw Invalido("El encabezado repite columnas.");
        return columnas;
    }

    private static FilaArchivo Armar(int numero, IReadOnlyList<string> columnas, IReadOnlyList<string> valores)
    {
        var datos = new Dictionary<string, string?>();
        for (var i = 0; i < columnas.Count; i++)
            if (columnas[i] != "") datos[columnas[i]] = i < valores.Count ? Recortar(valores[i]) : null;
        return new FilaArchivo(numero, datos);
    }

    private static string Recortar(string valor)
    {
        var limpio = valor.Trim();
        return limpio.Length > LongitudMaximaValor ? limpio[..LongitudMaximaValor] : limpio;
    }
}

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CajaComun.Domain.Enums;
using CajaComun.Domain.Reglas;

namespace CajaComun.Application.Importaciones;

public sealed record DatosAlta(
    string NumEmpleado, string Nombre, string Apellidos, string Rfc, DateOnly FechaNacimiento, DateOnly FechaIngreso,
    decimal SueldoQuincenal, string Clabe, string Departamento, decimal CuotaOrdinaria, decimal CuotaVoluntaria,
    bool VoluntarioActivo);

public sealed record DatosBaja(string NumEmpleado, DateOnly FechaBaja);

/// <summary>Resultado de validar un renglón sin consultar la base: lo que depende del padrón se revisa después.</summary>
public sealed record FilaValidada(
    TipoCambioImportacion? Movimiento, string NumEmpleado, DatosAlta? Alta, DatosBaja? Baja, IReadOnlyList<ErrorFila> Errores);

public static class ValidadorFilaImportacion
{
    public static readonly string[] ColumnasAlta =
    [
        "movimiento", "num_empleado", "nombre", "apellidos", "rfc", "fecha_nacimiento", "fecha_ingreso",
        "sueldo_quincenal", "clabe", "departamento", "cuota_ordinaria", "cuota_voluntaria", "voluntario_activo"
    ];

    public static readonly string[] Columnas = [.. ColumnasAlta, "fecha_baja"];

    private static readonly string[] FormatosFecha = ["yyyy-MM-dd", "dd/MM/yyyy", "d/M/yyyy"];

    /// <summary>Normaliza un nombre de columna: minúsculas, sin acentos y con guion bajo (NumEmpleado -> num_empleado).</summary>
    public static string NormalizarColumna(string nombre)
    {
        var sb = new StringBuilder();
        var d = nombre.Trim().Normalize(NormalizationForm.FormD);
        var anterior = '\0';
        foreach (var c in d)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsUpper(c) && char.IsLower(anterior)) sb.Append('_');
            sb.Append(c is ' ' or '-' ? '_' : char.ToLowerInvariant(c));
            anterior = c;
        }
        return sb.ToString() switch
        {
            "tipo" => "movimiento",
            "numero_empleado" or "no_empleado" or "numero_de_empleado" => "num_empleado",
            var texto => texto
        };
    }

    public static FilaValidada Validar(IReadOnlyDictionary<string, string?> fila, string prefijoCaja, DateOnly hoy)
    {
        var errores = new List<ErrorFila>();
        string Texto(string campo) => fila.GetValueOrDefault(campo)?.Trim() ?? "";

        var codigoMov = Texto("movimiento").ToUpperInvariant();
        TipoCambioImportacion? movimiento = codigoMov switch
        {
            "ALTA" => TipoCambioImportacion.Alta,
            "BAJA" => TipoCambioImportacion.Baja,
            _ => null
        };
        if (movimiento is null) errores.Add(new ErrorFila("movimiento", codigoMov == "" ? "REQUERIDO" : "MOVIMIENTO_INVALIDO"));

        var numero = Texto("num_empleado").ToUpperInvariant();
        if (numero == "") errores.Add(new ErrorFila("num_empleado", "REQUERIDO"));
        else if (!NumeroValido(numero)) errores.Add(new ErrorFila("num_empleado", "NUM_EMPLEADO_INVALIDO"));
        else if (!numero.StartsWith(prefijoCaja + "-", StringComparison.Ordinal))
            errores.Add(new ErrorFila("num_empleado", "NUM_EMPLEADO_PREFIJO"));

        if (movimiento == TipoCambioImportacion.Baja)
        {
            var fechaBaja = Fecha("fecha_baja", errores, Texto("fecha_baja"));
            if (errores.Count > 0 || fechaBaja is null) return new FilaValidada(movimiento, numero, null, null, errores);
            return new FilaValidada(movimiento, numero, null, new DatosBaja(numero, fechaBaja.Value), errores);
        }
        if (movimiento is null) return new FilaValidada(null, numero, null, null, errores);

        var nombre = Texto("nombre");
        var apellidos = Texto("apellidos");
        var rfc = Texto("rfc").ToUpperInvariant();
        var clabe = Texto("clabe");
        var depto = Texto("departamento");
        Requerido("nombre", nombre, 100, errores);
        Requerido("apellidos", apellidos, 100, errores);
        Requerido("departamento", depto, 100, errores);
        if (!ReglasAhorrador.RfcValido(rfc)) errores.Add(new ErrorFila("rfc", rfc == "" ? "REQUERIDO" : "RFC_INVALIDO"));
        if (!Clabe.EsValida(clabe)) errores.Add(new ErrorFila("clabe", clabe == "" ? "REQUERIDO" : "CLABE_INVALIDA"));

        var nacimiento = Fecha("fecha_nacimiento", errores, Texto("fecha_nacimiento"));
        if (nacimiento is { } n && (n >= hoy || n.Year < 1900)) errores.Add(new ErrorFila("fecha_nacimiento", "FECHA_INVALIDA"));
        var ingreso = Fecha("fecha_ingreso", errores, Texto("fecha_ingreso"));
        if (ingreso is { } i && nacimiento is { } n2 && i <= n2) errores.Add(new ErrorFila("fecha_ingreso", "FECHA_INVALIDA"));
        if (ingreso is { } i2 && i2.Year > 2100) errores.Add(new ErrorFila("fecha_ingreso", "FECHA_INVALIDA"));

        var sueldo = Monto("sueldo_quincenal", errores, Texto("sueldo_quincenal"), obligatorio: true);
        var ordinaria = Monto("cuota_ordinaria", errores, Texto("cuota_ordinaria"), obligatorio: true);
        var voluntaria = Monto("cuota_voluntaria", errores, Texto("cuota_voluntaria"), obligatorio: false) ?? 0m;

        var textoActivo = Texto("voluntario_activo");
        bool activo;
        if (textoActivo == "") activo = voluntaria > 0m;
        else if (!TryBooleano(textoActivo, out activo)) { errores.Add(new ErrorFila("voluntario_activo", "BOOLEANO_INVALIDO")); activo = false; }
        if (!activo && voluntaria > 0m) errores.Add(new ErrorFila("cuota_voluntaria", "VOLUNTARIO_INACTIVO"));

        if (errores.Count > 0) return new FilaValidada(movimiento, numero, null, null, errores);
        var alta = new DatosAlta(numero, nombre, apellidos, rfc, nacimiento!.Value, ingreso!.Value, sueldo!.Value, clabe,
            depto, ordinaria!.Value, voluntaria, activo);
        return new FilaValidada(movimiento, numero, alta, null, errores);
    }

    private static readonly Regex PatronNumero = new("^[A-Z]{2,5}-[0-9]{5}$", RegexOptions.Compiled);

    public static bool NumeroValido(string numero) => PatronNumero.IsMatch(numero);

    private static void Requerido(string campo, string valor, int max, List<ErrorFila> errores)
    {
        if (valor == "") errores.Add(new ErrorFila(campo, "REQUERIDO"));
        else if (valor.Length > max) errores.Add(new ErrorFila(campo, "LONGITUD_MAXIMA"));
    }

    private static DateOnly? Fecha(string campo, List<ErrorFila> errores, string texto)
    {
        if (texto == "") { errores.Add(new ErrorFila(campo, "REQUERIDO")); return null; }
        if (DateOnly.TryParseExact(texto, FormatosFecha, CultureInfo.InvariantCulture, DateTimeStyles.None, out var f)) return f;
        errores.Add(new ErrorFila(campo, "FECHA_INVALIDA"));
        return null;
    }

    private static decimal? Monto(string campo, List<ErrorFila> errores, string texto, bool obligatorio)
    {
        if (texto == "")
        {
            if (obligatorio) errores.Add(new ErrorFila(campo, "REQUERIDO"));
            return null;
        }
        var limpio = texto.Replace("$", "").Replace(" ", "");
        if (!decimal.TryParse(limpio, NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out var valor) || decimal.Round(valor, 2) != valor || Math.Abs(valor) >= 1_000_000_000_000m)
        {
            errores.Add(new ErrorFila(campo, "MONTO_INVALIDO"));
            return null;
        }
        if (valor < 0m) { errores.Add(new ErrorFila(campo, "MONTO_NEGATIVO")); return null; }
        return valor;
    }

    private static bool TryBooleano(string texto, out bool valor)
    {
        switch (texto.ToUpperInvariant())
        {
            case "SI" or "SÍ" or "S" or "1" or "TRUE" or "VERDADERO": valor = true; return true;
            case "NO" or "N" or "0" or "FALSE" or "FALSO": valor = false; return true;
            default: valor = false; return false;
        }
    }
}

namespace CajaComun.Application.Comun;

/// <summary>Códigos de error estables: la interfaz los traduce a ES/EN, nunca depende del texto.</summary>
public static class Codigos
{
    public const string Validacion = "VALIDACION";
    public const string NoAutenticado = "NO_AUTENTICADO";
    public const string SinPermiso = "SIN_PERMISO";
    public const string CredencialesInvalidas = "CREDENCIALES_INVALIDAS";
    public const string SesionInvalida = "SESION_INVALIDA";
    public const string CajaSinAcceso = "CAJA_SIN_ACCESO";
    public const string CajaNoEncontrada = "CAJA_NO_ENCONTRADA";
    public const string EmpresaNoEncontrada = "EMPRESA_NO_ENCONTRADA";
    public const string CajaDuplicada = "CAJA_DUPLICADA";
    public const string CajaInactiva = "CAJA_INACTIVA";
    public const string TasaFueraDeRango = "TASA_FUERA_DE_RANGO";
    public const string AhorradorNoEncontrado = "AHORRADOR_NO_ENCONTRADO";
    public const string NumEmpleadoDuplicado = "NUM_EMPLEADO_DUPLICADO";
    public const string AhorradorEnBaja = "AHORRADOR_EN_BAJA";
    public const string CuotaFechaInvalida = "CUOTA_FECHA_INVALIDA";
    public const string CuotaFechaDuplicada = "CUOTA_FECHA_DUPLICADA";
    public const string UsuarioNoEncontrado = "USUARIO_NO_ENCONTRADO";
    public const string CorreoDuplicado = "CORREO_DUPLICADO";
    public const string AutoModificacion = "AUTO_MODIFICACION";
    public const string UsuarioSinCajas = "USUARIO_SIN_CAJAS";
    public const string PeriodoNoEncontrado = "PERIODO_NO_ENCONTRADO";
    public const string PeriodoNoAbierto = "PERIODO_NO_ABIERTO";
    public const string PeriodoYaAplicado = "PERIODO_YA_APLICADO";
    public const string PeriodoNoSiguiente = "PERIODO_NO_SIGUIENTE";
    public const string SinPeriodoAbierto = "SIN_PERIODO_ABIERTO";
    public const string IncidenciaNoEncontrada = "INCIDENCIA_NO_ENCONTRADA";
    public const string IncidenciaDuplicada = "INCIDENCIA_DUPLICADA";
    public const string IncidenciaNoEliminable = "INCIDENCIA_NO_ELIMINABLE";
    public const string BajaConPrestamo = "BAJA_CON_PRESTAMO";
    public const string BajaConAval = "BAJA_CON_AVAL";
    public const string BajaFechaInvalida = "BAJA_FECHA_INVALIDA";
    public const string LayoutSinRegistros = "LAYOUT_SIN_REGISTROS";
    public const string ArchivoInvalido = "ARCHIVO_INVALIDO";
    public const string ArchivoDemasiadoGrande = "ARCHIVO_DEMASIADO_GRANDE";
    public const string ArchivoSinFilas = "ARCHIVO_SIN_FILAS";
    public const string ArchivoDemasiadasFilas = "ARCHIVO_DEMASIADAS_FILAS";
    public const string ImportacionNoEncontrada = "IMPORTACION_NO_ENCONTRADA";
    public const string ImportacionYaProcesada = "IMPORTACION_YA_PROCESADA";
    public const string ImportacionConErrores = "IMPORTACION_CON_ERRORES";
    public const string PrestamoNoEncontrado = "PRESTAMO_NO_ENCONTRADO";
    public const string PrestamoNoPendiente = "PRESTAMO_NO_PENDIENTE";
    public const string PrestamoPendienteExistente = "PRESTAMO_PENDIENTE_EXISTENTE";
    public const string AhorradorNoActivo = "AHORRADOR_NO_ACTIVO";
    public const string AvalRequerido = "AVAL_REQUERIDO";
    public const string AvalInsuficiente = "AVAL_INSUFICIENTE";
    public const string AvalInvalido = "AVAL_INVALIDO";
    public const string CicloNoEncontrado = "CICLO_NO_ENCONTRADO";
    public const string CicloYaCerrado = "CICLO_YA_CERRADO";
    public const string CicloNoCerrado = "CICLO_NO_CERRADO";
    public const string CicloPeriodosAbiertos = "CICLO_PERIODOS_ABIERTOS";
    public const string SpeiRenglonesInvalidos = "SPEI_RENGLONES_INVALIDOS";
    public const string SpeiSinDispersiones = "SPEI_SIN_DISPERSIONES";
    public const string TareaNoEncontrada = "TAREA_NO_ENCONTRADA";
    public const string TrabajoNoEncontrado = "TRABAJO_NO_ENCONTRADO";
    public const string TrabajoNoTerminado = "TRABAJO_NO_TERMINADO";
    public const string ConsultaNoEncontrada = "CONSULTA_NO_ENCONTRADA";
    public const string ConsultaRequiereTodasLasCajas = "CONSULTA_REQUIERE_TODAS_LAS_CAJAS";
    public const string FederacionNoDisponible = "FEDERACION_NO_DISPONIBLE";
    public const string ConsultaFederadaFallo = "CONSULTA_FEDERADA_FALLO";
    public const string ErrorInterno = "ERROR_INTERNO";
}

public class ErrorNegocio(string codigo, string mensaje, int estado = 400, IDictionary<string, string[]>? detalles = null)
    : Exception(mensaje)
{
    public string Codigo { get; } = codigo;
    public int Estado { get; } = estado;
    public IDictionary<string, string[]>? Detalles { get; } = detalles;

    public static ErrorNegocio NoEncontrado(string codigo, string mensaje) => new(codigo, mensaje, 404);
    public static ErrorNegocio Conflicto(string codigo, string mensaje) => new(codigo, mensaje, 409);
    public static ErrorNegocio Prohibido(string codigo, string mensaje) => new(codigo, mensaje, 403);
}

namespace CajaComun.Domain.Enums;

// En la base de datos cada valor se guarda en MAYÚSCULAS con guion bajo (AhorroOrdinario -> AHORRO_ORDINARIO).

public enum RolUsuario { Administrador, Capturista, Consulta }

public enum FrecuenciaNomina { Quincenal, Semanal }

public enum EstadoAhorrador { Activo, Licencia, Baja }

public enum EstadoPeriodo { Abierto, Aplicado, Cerrado }

public enum TipoMovimiento
{
    AhorroOrdinario,
    AhorroVoluntario,
    DesembolsoPrestamo,
    AbonoCapital,
    AbonoInteres,
    /// <summary>Rendimiento asignado al cerrar el ciclo. No forma parte del ahorro acumulado.</summary>
    InteresAnual,
    /// <summary>Pago del ahorro acumulado (al cerrar el ciclo o al darse de baja). Deja el ahorro en cero.</summary>
    LiquidacionCiclo
}

public enum GarantiaPrestamo { Ahorro80, PagareAval }

public enum EstadoPrestamo { Pendiente, Vigente, Liquidado, Rechazado }

public enum EstadoAmortizacion { Proyectado, Programado, Aplicado }

public enum TipoIncidencia { Licencia, Incapacidad, Baja, CambioCuota, SuspendeVoluntario }

public enum FormatoArchivo { Xlsx, Xml, Txt, Csv }

public enum EstadoImportacion { Previa, Aplicada, Cancelada }

public enum TipoCambioImportacion { Alta, Baja, SinCambio }

public enum ResultadoValidacion { Valido, Error }

public enum TipoLayout { Nomina, Spei }

public enum EstadoCiclo { Abierto, EnCierre, Cerrado }

public enum FrecuenciaTarea { Diaria, Quincenal, Mensual }

public enum EstadoEjecucion { EnCurso, Exito, Error }

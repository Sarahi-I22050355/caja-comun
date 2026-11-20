using CajaComun.Domain.Enums;

namespace CajaComun.Domain.Entities;

public class Importacion
{
    public int Id { get; set; }
    public int CajaId { get; set; }
    public Caja Caja { get; set; } = null!;
    public string Archivo { get; set; } = "";
    public FormatoArchivo Formato { get; set; }
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public DateTime Fecha { get; set; }
    public EstadoImportacion Estado { get; set; }
    public List<ImportacionDetalle> Detalles { get; set; } = [];
}

public class ImportacionDetalle
{
    public long Id { get; set; }
    public int ImportacionId { get; set; }
    public Importacion Importacion { get; set; } = null!;
    public int NumeroFila { get; set; }
    public TipoCambioImportacion Tipo { get; set; }
    public string NumEmpleado { get; set; } = "";
    /// <summary>Fila original en JSON.</summary>
    public string Datos { get; set; } = "{}";
    public ResultadoValidacion Validacion { get; set; }
    public string? MensajeError { get; set; }
}

public class Ciclo
{
    public int Id { get; set; }
    public int CajaId { get; set; }
    public Caja Caja { get; set; } = null!;
    public int Anio { get; set; }
    public EstadoCiclo Estado { get; set; }
    public decimal InteresTotal { get; set; }
    public DateTime? FechaCierre { get; set; }
}

public class CicloDistribucion
{
    public int CicloId { get; set; }
    public Ciclo Ciclo { get; set; } = null!;
    public int AhorradorId { get; set; }
    public Ahorrador Ahorrador { get; set; } = null!;
    public decimal SaldoDias { get; set; }
    public decimal SaldoPromedio { get; set; }
    public int DiasActivos { get; set; }
    public decimal Participacion { get; set; }
    public decimal InteresAsignado { get; set; }
    /// <summary>Ahorro que se liquida al cerrar el ciclo (cero para quien ya se liquidó por baja).</summary>
    public decimal AhorroLiquidado { get; set; }
    /// <summary>Ahorro que se conserva como garantía de un préstamo vigente y pasa al ciclo siguiente.</summary>
    public decimal AhorroRetenido { get; set; }
}

public class LayoutGenerado
{
    public int Id { get; set; }
    public TipoLayout Tipo { get; set; }
    public int CajaId { get; set; }
    public Caja Caja { get; set; } = null!;
    public int? PeriodoId { get; set; }
    public int? CicloId { get; set; }
    public string Formato { get; set; } = "";
    public string NombreArchivo { get; set; } = "";
    public int Registros { get; set; }
    public decimal ImporteTotal { get; set; }
    public string? SumaControl { get; set; }
    public int UsuarioId { get; set; }
    public DateTime Fecha { get; set; }
}

public class TareaProgramada
{
    public int Id { get; set; }
    public string Clave { get; set; } = "";
    public string Nombre { get; set; } = "";
    public FrecuenciaTarea Frecuencia { get; set; }
    public TimeOnly Hora { get; set; }
    public bool Activa { get; set; } = true;
    public DateTime? UltimaEjecucion { get; set; }
    public DateTime? ProximaEjecucion { get; set; }
}

public class TareaEjecucion
{
    public long Id { get; set; }
    public int TareaId { get; set; }
    public TareaProgramada Tarea { get; set; } = null!;
    public DateTime Inicio { get; set; }
    public DateTime? Fin { get; set; }
    public EstadoEjecucion Estado { get; set; }
    public string? Resumen { get; set; }
}

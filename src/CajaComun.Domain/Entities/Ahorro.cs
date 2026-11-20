using CajaComun.Domain.Enums;

namespace CajaComun.Domain.Entities;

public class Ahorrador
{
    public int Id { get; set; }
    public int CajaId { get; set; }
    public Caja Caja { get; set; } = null!;
    public string NumEmpleado { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Apellidos { get; set; } = "";
    public string Rfc { get; set; } = "";
    public DateOnly FechaNacimiento { get; set; }
    public DateOnly FechaIngreso { get; set; }
    public DateOnly? FechaBaja { get; set; }
    public EstadoAhorrador Estado { get; set; } = EstadoAhorrador.Activo;
    public decimal SueldoQuincenal { get; set; }
    public string Clabe { get; set; } = "";
    public string Departamento { get; set; } = "";
}

/// <summary>Historial de cuotas: la vigente en una fecha es la de mayor vigente_desde menor o igual a ella.</summary>
public class CuotaAhorro
{
    public int Id { get; set; }
    public int AhorradorId { get; set; }
    public Ahorrador Ahorrador { get; set; } = null!;
    public decimal CuotaOrdinaria { get; set; }
    public decimal CuotaVoluntaria { get; set; }
    public bool VoluntarioActivo { get; set; }
    public DateOnly VigenteDesde { get; set; }
}

public class Periodo
{
    public int Id { get; set; }
    public int CajaId { get; set; }
    public Caja Caja { get; set; } = null!;
    public int Anio { get; set; }
    public int Numero { get; set; }
    public DateOnly FechaInicio { get; set; }
    /// <summary>Fecha de pago de la nómina del periodo.</summary>
    public DateOnly FechaFin { get; set; }
    public EstadoPeriodo Estado { get; set; } = EstadoPeriodo.Abierto;
}

public class Movimiento
{
    public long Id { get; set; }
    public int CajaId { get; set; }
    public int AhorradorId { get; set; }
    public int? PeriodoId { get; set; }
    public int? PrestamoId { get; set; }
    public DateOnly Fecha { get; set; }
    public TipoMovimiento Tipo { get; set; }
    /// <summary>Siempre positivo; el sentido lo da el tipo.</summary>
    public decimal Monto { get; set; }
    public string? Referencia { get; set; }
}

public class Incidencia
{
    public int Id { get; set; }
    public int AhorradorId { get; set; }
    public Ahorrador Ahorrador { get; set; } = null!;
    public int PeriodoId { get; set; }
    public Periodo Periodo { get; set; } = null!;
    public TipoIncidencia Tipo { get; set; }
    public decimal? Valor { get; set; }
    public string? Nota { get; set; }
}

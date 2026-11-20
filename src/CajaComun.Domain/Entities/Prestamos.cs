using CajaComun.Domain.Enums;

namespace CajaComun.Domain.Entities;

public class Prestamo
{
    public int Id { get; set; }
    /// <summary>FOL-AAAA-NNN, único en todo el sistema.</summary>
    public string Folio { get; set; } = "";
    public int CajaId { get; set; }
    public Caja Caja { get; set; } = null!;
    public int AhorradorId { get; set; }
    public Ahorrador Ahorrador { get; set; } = null!;
    public decimal Monto { get; set; }
    public int PlazoQuincenas { get; set; }
    public decimal TasaMensual { get; set; }
    public GarantiaPrestamo Garantia { get; set; }
    public int? AvalAhorradorId { get; set; }
    public Ahorrador? Aval { get; set; }
    /// <summary>Parte del monto que respalda el aval (cero cuando la garantía es el propio ahorro).</summary>
    public decimal ExcedenteAval { get; set; }
    public EstadoPrestamo Estado { get; set; }
    public DateOnly FechaSolicitud { get; set; }
    public DateOnly? FechaAutorizacion { get; set; }
    public int? AutorizadoPor { get; set; }
    public Usuario? Autorizador { get; set; }
    public List<PrestamoAmortizacion> Amortizacion { get; set; } = [];
}

public class PrestamoAmortizacion
{
    public long Id { get; set; }
    public int PrestamoId { get; set; }
    public Prestamo Prestamo { get; set; } = null!;
    public int Numero { get; set; }
    public int PeriodoId { get; set; }
    public Periodo Periodo { get; set; } = null!;
    public DateOnly Fecha { get; set; }
    public decimal Capital { get; set; }
    public decimal Interes { get; set; }
    public decimal Total { get; set; }
    public decimal SaldoDespues { get; set; }
    public EstadoAmortizacion Estado { get; set; }
}

public class InteresDevengado
{
    public long Id { get; set; }
    public int PrestamoId { get; set; }
    public Prestamo Prestamo { get; set; } = null!;
    public int PeriodoId { get; set; }
    public Periodo Periodo { get; set; } = null!;
    public DateOnly FechaCorte { get; set; }
    public decimal SaldoInsoluto { get; set; }
    public decimal Interes { get; set; }
}

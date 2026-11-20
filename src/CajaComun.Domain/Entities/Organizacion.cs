using CajaComun.Domain.Enums;

namespace CajaComun.Domain.Entities;

public class Empresa
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public bool Activa { get; set; } = true;
    public List<Caja> Cajas { get; set; } = [];
}

public class Caja
{
    public int Id { get; set; }
    public int EmpresaId { get; set; }
    public Empresa Empresa { get; set; } = null!;
    public string Nombre { get; set; } = "";
    /// <summary>Prefijo del número de empleado (GIM, ACE, TDB).</summary>
    public string Prefijo { get; set; } = "";
    /// <summary>Entre 1.00 y 2.00 (validado en dominio y con CHECK en la BD).</summary>
    public decimal TasaInteresMensual { get; set; }
    public FrecuenciaNomina FrecuenciaNomina { get; set; } = FrecuenciaNomina.Quincenal;
    public DateOnly InicioCiclo { get; set; }
    public DateOnly FinCiclo { get; set; }
    public bool Activa { get; set; } = true;
}

public class Usuario
{
    public int Id { get; set; }
    public string Nombre { get; set; } = "";
    public string Correo { get; set; } = "";
    public string HashPassword { get; set; } = "";
    public RolUsuario Rol { get; set; }
    /// <summary>Si es verdadero ve todas las cajas sin necesidad de filas en usuario_caja.</summary>
    public bool AccesoTodasCajas { get; set; }
    public bool Activo { get; set; } = true;
    public DateTime? UltimoAcceso { get; set; }
    public List<UsuarioCaja> Cajas { get; set; } = [];
}

public class UsuarioCaja
{
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;
    public int CajaId { get; set; }
    public Caja Caja { get; set; } = null!;
}

public class RefreshToken
{
    public long Id { get; set; }
    public int UsuarioId { get; set; }
    public Usuario Usuario { get; set; } = null!;
    /// <summary>Hash SHA-256 del token; el token en claro solo vive en la cookie.</summary>
    public string TokenHash { get; set; } = "";
    public DateTime Creado { get; set; }
    public DateTime Expira { get; set; }
    public DateTime? Revocado { get; set; }
}

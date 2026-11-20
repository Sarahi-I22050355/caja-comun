using CajaComun.Domain.Enums;

namespace CajaComun.Domain.Reglas;

public enum AccionSistema
{
    /// <summary>Consultar y exportar.</summary>
    Leer,
    /// <summary>Altas/bajas, nómina, incidencias, ahorro voluntario y solicitud de préstamos.</summary>
    Capturar,
    /// <summary>Autorizar préstamos, cerrar ciclos, administrar usuarios, cajas y tareas.</summary>
    Administrar
}

public static class Permisos
{
    public static bool Puede(RolUsuario rol, AccionSistema accion) => (rol, accion) switch
    {
        (RolUsuario.Administrador, _) => true,
        (RolUsuario.Capturista, AccionSistema.Leer or AccionSistema.Capturar) => true,
        (RolUsuario.Consulta, AccionSistema.Leer) => true,
        _ => false
    };

    /// <summary>Un usuario ve una caja si tiene acceso global o la tiene asignada.</summary>
    public static bool PuedeVerCaja(bool accesoTodasCajas, IReadOnlyCollection<int> cajasAsignadas, int cajaId) =>
        accesoTodasCajas || cajasAsignadas.Contains(cajaId);
}

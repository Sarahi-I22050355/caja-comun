using CajaComun.Application.Nomina;
using CajaComun.Domain.Enums;

namespace CajaComun.Application.Trabajos;

public enum EstadoTrabajo { Pendiente, EnCurso, Exito, Error }

public sealed record TrabajoDto(
    string Id, string Tipo, EstadoTrabajo Estado, DateTime Creado, DateTime? Inicio, DateTime? Fin, object? Resultado,
    string? CodigoError, string? MensajeError, string? Archivo);

/// <summary>Lo que devuelve un trabajo: datos para mostrar y, opcionalmente, un archivo para descargar.</summary>
public sealed record ResultadoTrabajo(object? Datos, ArchivoGenerado? Archivo = null);

/// <summary>
/// Cola de trabajos largos (cierre de ciclo, reportes grandes, ejecución de tareas): las peticiones responden 202 con el
/// id y el estado se consulta aparte. El trabajo corre con el usuario que lo pidió.
/// </summary>
public interface IColaTrabajos
{
    Task<TrabajoDto> EncolarAsync(
        string tipo, int usuarioId, RolUsuario rol,
        Func<IServiceProvider, CancellationToken, Task<ResultadoTrabajo>> trabajo, CancellationToken ct = default);

    /// <summary>El trabajo de otro usuario solo lo ve un Administrador; devuelve null si no existe o no es visible.</summary>
    Task<TrabajoDto?> ObtenerAsync(string id, int usuarioId, RolUsuario rol, CancellationToken ct = default);

    Task<ArchivoGenerado?> ArchivoAsync(string id, int usuarioId, RolUsuario rol, CancellationToken ct = default);
}

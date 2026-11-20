using CajaComun.Domain.Enums;

namespace CajaComun.Application.Comun;

/// <summary>Quién hace la petición (lo resuelve la capa web a partir del token).</summary>
public interface IUsuarioActual
{
    bool EstaAutenticado { get; }
    int Id { get; }
    RolUsuario Rol { get; }
}

/// <summary>
/// Usuario con el que corre un trabajo en segundo plano (no hay petición HTTP). Lo fija el procesador de la cola
/// antes de ejecutar el trabajo; la tarea programada corre con acceso global como el sistema.
/// </summary>
public sealed class ContextoTrabajo
{
    public bool Activo { get; private set; }
    public int UsuarioId { get; private set; }
    public RolUsuario Rol { get; private set; }

    public void Fijar(int usuarioId, RolUsuario rol)
    {
        UsuarioId = usuarioId;
        Rol = rol;
        Activo = true;
    }
}

/// <summary>
/// Caché de resultados costosos (dashboard, reportes). Cada entrada depende de la versión de datos de su caja: al
/// cambiar los datos se invalida explícitamente con <see cref="InvalidarAsync"/> en lugar de esperar al vencimiento.
/// Si el caché no está disponible, se calcula el resultado sin guardarlo.
/// </summary>
public interface ICacheDatos
{
    Task<T> ObtenerOCrearAsync<T>(string clave, int? cajaId, TimeSpan vigencia, Func<Task<T>> crear);
    Task InvalidarAsync(int cajaId);
}

/// <summary>Sin caché: siempre calcula. Es lo que usan las pruebas y quien no configura Redis.</summary>
public sealed class SinCache : ICacheDatos
{
    public Task<T> ObtenerOCrearAsync<T>(string clave, int? cajaId, TimeSpan vigencia, Func<Task<T>> crear) => crear();
    public Task InvalidarAsync(int cajaId) => Task.CompletedTask;
}

/// <summary>Cajas que el usuario actual puede ver: acceso global o las asignadas en usuario_caja.</summary>
public interface ICajaAcceso
{
    Task<IReadOnlyList<int>> CajasPermitidasAsync(CancellationToken ct = default);

    /// <summary>Lanza CAJA_SIN_ACCESO si la caja no está entre las permitidas.</summary>
    Task ExigirAsync(int cajaId, CancellationToken ct = default);
}

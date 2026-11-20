using System.Security.Claims;
using CajaComun.Application.Comun;
using CajaComun.Domain.Enums;

namespace CajaComun.Api.Infraestructura;

/// <summary>
/// El usuario sale del token de la petición. En un trabajo en segundo plano no hay petición: el procesador de la cola
/// fija el usuario que pidió el trabajo en <see cref="ContextoTrabajo"/> y ese es el que se usa.
/// </summary>
public sealed class UsuarioActualHttp(IHttpContextAccessor http, ContextoTrabajo trabajo) : IUsuarioActual
{
    private ClaimsPrincipal? Principal => http.HttpContext?.User;

    public bool EstaAutenticado => trabajo.Activo || (Principal?.Identity?.IsAuthenticated == true && Id > 0);

    public int Id => trabajo.Activo ? trabajo.UsuarioId : int.TryParse(Principal?.FindFirstValue("sub"), out var id) ? id : 0;

    public RolUsuario Rol => trabajo.Activo
        ? trabajo.Rol
        : Enum.TryParse<RolUsuario>(Principal?.FindFirstValue("role"), out var rol)
            ? rol
            : throw new InvalidOperationException("La petición no trae un rol válido.");
}

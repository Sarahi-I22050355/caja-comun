using CajaComun.Application.Comun;
using CajaComun.Domain.Reglas;
using CajaComun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CajaComun.Infrastructure.Servicios;

/// <summary>
/// Cajas visibles para el usuario de la petición. El resultado se calcula una vez por petición; se consulta a la
/// base (no al token) para que quitar una caja a un usuario surta efecto de inmediato.
/// </summary>
public sealed class CajaAcceso(CajaComunDbContext db, IUsuarioActual usuario) : ICajaAcceso
{
    private IReadOnlyList<int>? _permitidas;

    public async Task<IReadOnlyList<int>> CajasPermitidasAsync(CancellationToken ct = default)
    {
        if (_permitidas is not null) return _permitidas;
        if (!usuario.EstaAutenticado) return _permitidas = [];

        var datos = await db.Usuarios.AsNoTracking()
            .Where(u => u.Id == usuario.Id && u.Activo)
            .Select(u => new { u.AccesoTodasCajas })
            .FirstOrDefaultAsync(ct);

        if (datos is null) return _permitidas = [];

        _permitidas = datos.AccesoTodasCajas
            ? await db.Cajas.AsNoTracking().Select(c => c.Id).ToListAsync(ct)
            : await db.UsuarioCajas.AsNoTracking().Where(x => x.UsuarioId == usuario.Id).Select(x => x.CajaId).ToListAsync(ct);
        return _permitidas;
    }

    public async Task ExigirAsync(int cajaId, CancellationToken ct = default)
    {
        var permitidas = await CajasPermitidasAsync(ct);
        if (!Permisos.PuedeVerCaja(false, permitidas, cajaId))
            throw ErrorNegocio.Prohibido(Codigos.CajaSinAcceso, "No tienes acceso a esa caja.");
    }
}

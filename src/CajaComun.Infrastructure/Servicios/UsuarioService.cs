using CajaComun.Application.Auth;
using CajaComun.Application.Comun;
using CajaComun.Application.Usuarios;
using CajaComun.Domain.Entities;
using CajaComun.Domain.Enums;
using CajaComun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace CajaComun.Infrastructure.Servicios;

public sealed class UsuarioService(CajaComunDbContext db, IUsuarioActual actual, TimeProvider reloj) : IUsuarioService
{
    public Task<IReadOnlyList<UsuarioDto>> ListarAsync(CancellationToken ct = default) => ConsultarAsync(null, ct);

    public async Task<UsuarioDto> ObtenerAsync(int id, CancellationToken ct = default) =>
        (await ConsultarAsync(id, ct)).FirstOrDefault()
        ?? throw ErrorNegocio.NoEncontrado(Codigos.UsuarioNoEncontrado, "El usuario no existe.");

    // Dos consultas en total (cajas y usuarios con sus asignaciones), sin consulta por usuario.
    private async Task<IReadOnlyList<UsuarioDto>> ConsultarAsync(int? id, CancellationToken ct)
    {
        var cajas = await db.Cajas.AsNoTracking().OrderBy(c => c.Nombre)
            .Select(c => new { Dto = new CajaAccesoDto(c.Id, c.Nombre, c.Empresa.Nombre) })
            .ToListAsync(ct);
        var consulta = db.Usuarios.AsNoTracking();
        if (id is { } uid) consulta = consulta.Where(u => u.Id == uid);
        var usuarios = await consulta.OrderBy(u => u.Nombre)
            .Select(u => new
            {
                u.Id, u.Nombre, u.Correo, u.Rol, u.AccesoTodasCajas, u.Activo, u.UltimoAcceso,
                Asignadas = u.Cajas.Select(x => x.CajaId).ToList()
            })
            .ToListAsync(ct);
        return usuarios.Select(u => new UsuarioDto(
            u.Id, u.Nombre, u.Correo, u.Rol, u.AccesoTodasCajas, u.Activo, u.UltimoAcceso,
            cajas.Where(c => u.AccesoTodasCajas || u.Asignadas.Contains(c.Dto.Id)).Select(c => c.Dto).ToList())).ToList();
    }

    public async Task<UsuarioDto> CrearAsync(CrearUsuarioRequest request, CancellationToken ct = default)
    {
        var correo = request.Correo.Trim().ToLowerInvariant();
        if (await db.Usuarios.AnyAsync(u => u.Correo == correo, ct))
            throw ErrorNegocio.Conflicto(Codigos.CorreoDuplicado, "Ya existe un usuario con ese correo.");

        var (todas, cajaIds) = await NormalizarAccesoAsync(request.Rol, request.AccesoTodasCajas, request.CajaIds, ct);
        var usuario = new Usuario
        {
            Nombre = request.Nombre.Trim(),
            Correo = correo,
            HashPassword = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 11),
            Rol = request.Rol,
            AccesoTodasCajas = todas,
            Activo = true
        };
        foreach (var cajaId in cajaIds) usuario.Cajas.Add(new UsuarioCaja { CajaId = cajaId });
        db.Usuarios.Add(usuario);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
        {
            throw ErrorNegocio.Conflicto(Codigos.CorreoDuplicado, "Ya existe un usuario con ese correo.");
        }
        return await ObtenerAsync(usuario.Id, ct);
    }

    public async Task<UsuarioDto> ActualizarAsync(int id, ActualizarUsuarioRequest request, CancellationToken ct = default)
    {
        var usuario = await db.Usuarios.Include(u => u.Cajas).FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw ErrorNegocio.NoEncontrado(Codigos.UsuarioNoEncontrado, "El usuario no existe.");

        // Evita que el último administrador se quite el rol o se desactive a sí mismo y deje el sistema sin gestor.
        if (id == actual.Id && (request.Rol != usuario.Rol || !request.Activo))
            throw ErrorNegocio.Conflicto(Codigos.AutoModificacion, "No puedes cambiar tu propio rol ni desactivarte.");

        var (todas, cajaIds) = await NormalizarAccesoAsync(request.Rol, request.AccesoTodasCajas, request.CajaIds, ct);
        var desactivado = usuario.Activo && !request.Activo;

        usuario.Nombre = request.Nombre.Trim();
        usuario.Rol = request.Rol;
        usuario.Activo = request.Activo;
        usuario.AccesoTodasCajas = todas;

        var actuales = usuario.Cajas.Select(c => c.CajaId).ToHashSet();
        foreach (var quitar in usuario.Cajas.Where(c => !cajaIds.Contains(c.CajaId)).ToList()) usuario.Cajas.Remove(quitar);
        foreach (var agregar in cajaIds.Where(c => !actuales.Contains(c))) usuario.Cajas.Add(new UsuarioCaja { UsuarioId = id, CajaId = agregar });

        await db.SaveChangesAsync(ct);
        if (desactivado) await RevocarSesionesAsync(id, ct);
        return await ObtenerAsync(id, ct);
    }

    public async Task RestablecerPasswordAsync(int id, RestablecerPasswordRequest request, CancellationToken ct = default)
    {
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw ErrorNegocio.NoEncontrado(Codigos.UsuarioNoEncontrado, "El usuario no existe.");
        usuario.HashPassword = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 11);
        await db.SaveChangesAsync(ct);
        // Con la contraseña nueva las sesiones abiertas dejan de ser de fiar.
        await RevocarSesionesAsync(id, ct);
    }

    /// <summary>El administrador siempre ve todas las cajas; los demás roles necesitan acceso global o al menos una caja.</summary>
    private async Task<(bool Todas, IReadOnlyCollection<int> CajaIds)> NormalizarAccesoAsync(
        RolUsuario rol, bool accesoTodas, IReadOnlyList<int>? cajaIds, CancellationToken ct)
    {
        if (rol == RolUsuario.Administrador) accesoTodas = true;
        if (accesoTodas) return (true, []);

        var ids = (cajaIds ?? []).Distinct().ToList();
        if (ids.Count == 0)
            throw new ErrorNegocio(Codigos.UsuarioSinCajas, "Asigna al menos una caja al usuario.", 400,
                new Dictionary<string, string[]> { ["cajaIds"] = [Codigos.UsuarioSinCajas] });

        var existentes = await db.Cajas.CountAsync(c => ids.Contains(c.Id), ct);
        if (existentes != ids.Count)
            throw ErrorNegocio.NoEncontrado(Codigos.CajaNoEncontrada, "Alguna de las cajas no existe.");
        return (false, ids);
    }

    private async Task RevocarSesionesAsync(int usuarioId, CancellationToken ct)
    {
        var ahora = reloj.GetUtcNow().UtcDateTime;
        await db.RefreshTokens.Where(t => t.UsuarioId == usuarioId && t.Revocado == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Revocado, ahora), ct);
    }
}

using CajaComun.Application.Auth;
using CajaComun.Application.Comun;
using CajaComun.Domain.Entities;
using CajaComun.Infrastructure.Persistence;
using CajaComun.Infrastructure.Seguridad;
using Microsoft.EntityFrameworkCore;

namespace CajaComun.Infrastructure.Servicios;

public sealed class AuthService(CajaComunDbContext db, TokenService tokens, IUsuarioActual actual, TimeProvider reloj)
    : IAuthService
{
    // Hash de relleno: si el correo no existe se verifica igual para que el tiempo de respuesta no delate cuentas.
    private static readonly string HashRelleno = BCrypt.Net.BCrypt.HashPassword("relleno-sin-uso", workFactor: 11);

    // Dos peticiones simultáneas con la misma cookie (recarga, pestañas) no deben contar como robo de token.
    private static readonly TimeSpan Gracia = TimeSpan.FromSeconds(15);

    public async Task<SesionIniciada> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        var correo = request.Correo.Trim();
        var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Correo == correo, ct);

        var passwordOk = BCrypt.Net.BCrypt.Verify(request.Password, usuario?.HashPassword ?? HashRelleno);
        if (usuario is null || !usuario.Activo || !passwordOk)
            throw new ErrorNegocio(Codigos.CredencialesInvalidas, "Correo o contraseña incorrectos.", 401);

        var ahora = reloj.GetUtcNow().UtcDateTime;
        usuario.UltimoAcceso = ahora;
        var sesion = await EmitirAsync(usuario, ahora, ct);
        await db.SaveChangesAsync(ct);
        return sesion;
    }

    public async Task<SesionIniciada> RefrescarAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = TokenService.Hash(refreshToken);
        var guardado = await db.RefreshTokens.Include(t => t.Usuario).FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        var ahora = reloj.GetUtcNow().UtcDateTime;

        if (guardado is null)
            throw SesionInvalida();

        if (guardado.Revocado is { } revocado)
        {
            // Un token ya rotado que reaparece fuera de la gracia indica que pudo copiarse: se cierran todas las sesiones.
            if (ahora - revocado > Gracia)
            {
                await db.RefreshTokens
                    .Where(t => t.UsuarioId == guardado.UsuarioId && t.Revocado == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.Revocado, ahora), ct);
            }
            throw SesionInvalida();
        }

        if (guardado.Expira <= ahora || !guardado.Usuario.Activo)
            throw SesionInvalida();

        guardado.Revocado = ahora;
        var sesion = await EmitirAsync(guardado.Usuario, ahora, ct);
        await db.SaveChangesAsync(ct);
        return sesion;
    }

    public async Task CerrarSesionAsync(string? refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(refreshToken)) return;
        var hash = TokenService.Hash(refreshToken);
        var ahora = reloj.GetUtcNow().UtcDateTime;
        await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.Revocado == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Revocado, ahora), ct);
    }

    public async Task<UsuarioActualDto> ObtenerActualAsync(CancellationToken ct = default)
    {
        var usuario = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actual.Id && u.Activo, ct)
            ?? throw new ErrorNegocio(Codigos.NoAutenticado, "La sesión ya no es válida.", 401);
        return await ArmarUsuarioAsync(usuario, ct);
    }

    private async Task<SesionIniciada> EmitirAsync(Usuario usuario, DateTime ahora, CancellationToken ct)
    {
        var (acceso, segundos) = tokens.CrearAcceso(usuario, ahora);
        var refresh = tokens.CrearRefresh(ahora);
        db.RefreshTokens.Add(new RefreshToken
        {
            UsuarioId = usuario.Id, TokenHash = refresh.Hash, Creado = ahora, Expira = refresh.Expira
        });
        var dto = await ArmarUsuarioAsync(usuario, ct);
        return new SesionIniciada(new SesionDto(acceso, segundos, dto), refresh.Token, refresh.Expira);
    }

    private async Task<UsuarioActualDto> ArmarUsuarioAsync(Usuario usuario, CancellationToken ct)
    {
        var consulta = usuario.AccesoTodasCajas
            ? db.Cajas.AsNoTracking()
            : db.Cajas.AsNoTracking().Where(c => db.UsuarioCajas.Any(uc => uc.UsuarioId == usuario.Id && uc.CajaId == c.Id));
        var cajas = await consulta.OrderBy(c => c.Nombre)
            .Select(c => new CajaAccesoDto(c.Id, c.Nombre, c.Empresa.Nombre))
            .ToListAsync(ct);
        return new UsuarioActualDto(usuario.Id, usuario.Nombre, usuario.Correo, usuario.Rol, usuario.AccesoTodasCajas, cajas);
    }

    private static ErrorNegocio SesionInvalida() =>
        new(Codigos.SesionInvalida, "La sesión expiró; inicia sesión de nuevo.", 401);
}

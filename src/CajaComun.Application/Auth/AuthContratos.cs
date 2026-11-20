using CajaComun.Domain.Enums;
using FluentValidation;

namespace CajaComun.Application.Auth;

public sealed record LoginRequest(string Correo, string Password);

public sealed record CajaAccesoDto(int Id, string Nombre, string Empresa);

public sealed record UsuarioActualDto(
    int Id, string Nombre, string Correo, RolUsuario Rol, bool AccesoTodasCajas, IReadOnlyList<CajaAccesoDto> Cajas);

public sealed record SesionDto(string AccessToken, int ExpiraEnSegundos, UsuarioActualDto Usuario);

/// <summary>Resultado interno: el token de refresco viaja en cookie, nunca en el cuerpo de la respuesta.</summary>
public sealed record SesionIniciada(SesionDto Sesion, string RefreshToken, DateTime RefreshExpira);

public interface IAuthService
{
    Task<SesionIniciada> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<SesionIniciada> RefrescarAsync(string refreshToken, CancellationToken ct = default);
    Task CerrarSesionAsync(string? refreshToken, CancellationToken ct = default);
    Task<UsuarioActualDto> ObtenerActualAsync(CancellationToken ct = default);
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Correo).NotEmpty().WithErrorCode("REQUERIDO").MaximumLength(150).WithErrorCode("LONGITUD_MAXIMA");
        RuleFor(x => x.Password).NotEmpty().WithErrorCode("REQUERIDO").MaximumLength(200).WithErrorCode("LONGITUD_MAXIMA");
    }
}

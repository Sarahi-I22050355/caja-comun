using System.Text.RegularExpressions;
using CajaComun.Application.Auth;
using CajaComun.Domain.Enums;
using FluentValidation;

namespace CajaComun.Application.Usuarios;

public sealed record UsuarioDto(
    int Id, string Nombre, string Correo, RolUsuario Rol, bool AccesoTodasCajas, bool Activo,
    DateTime? UltimoAcceso, IReadOnlyList<CajaAccesoDto> Cajas);

public sealed record CrearUsuarioRequest(
    string Nombre, string Correo, string Password, RolUsuario Rol, bool AccesoTodasCajas, IReadOnlyList<int>? CajaIds);

public sealed record ActualizarUsuarioRequest(
    string Nombre, RolUsuario Rol, bool Activo, bool AccesoTodasCajas, IReadOnlyList<int>? CajaIds);

public sealed record RestablecerPasswordRequest(string Password);

public interface IUsuarioService
{
    Task<IReadOnlyList<UsuarioDto>> ListarAsync(CancellationToken ct = default);
    Task<UsuarioDto> ObtenerAsync(int id, CancellationToken ct = default);
    Task<UsuarioDto> CrearAsync(CrearUsuarioRequest request, CancellationToken ct = default);
    Task<UsuarioDto> ActualizarAsync(int id, ActualizarUsuarioRequest request, CancellationToken ct = default);
    Task RestablecerPasswordAsync(int id, RestablecerPasswordRequest request, CancellationToken ct = default);
}

public static class PoliticaPassword
{
    public const int LongitudMinima = 10;

    private static readonly Regex Mayuscula = new("[A-ZÑ]", RegexOptions.Compiled);
    private static readonly Regex Minuscula = new("[a-zñ]", RegexOptions.Compiled);
    private static readonly Regex Digito = new("[0-9]", RegexOptions.Compiled);

    /// <summary>Mínimo 10 caracteres con mayúscula, minúscula y dígito.</summary>
    public static bool Cumple(string? password) =>
        password is { Length: >= LongitudMinima and <= 100 }
        && Mayuscula.IsMatch(password) && Minuscula.IsMatch(password) && Digito.IsMatch(password);
}

public sealed class CrearUsuarioRequestValidator : AbstractValidator<CrearUsuarioRequest>
{
    public CrearUsuarioRequestValidator()
    {
        RuleFor(x => x.Nombre).NotEmpty().WithErrorCode("REQUERIDO").MaximumLength(150).WithErrorCode("LONGITUD_MAXIMA");
        RuleFor(x => x.Correo).NotEmpty().WithErrorCode("REQUERIDO").EmailAddress().WithErrorCode("CORREO_INVALIDO")
            .MaximumLength(150).WithErrorCode("LONGITUD_MAXIMA");
        RuleFor(x => x.Password).Must(PoliticaPassword.Cumple).WithErrorCode("PASSWORD_DEBIL");
        RuleFor(x => x.Rol).IsInEnum().WithErrorCode("VALOR_INVALIDO");
    }
}

public sealed class ActualizarUsuarioRequestValidator : AbstractValidator<ActualizarUsuarioRequest>
{
    public ActualizarUsuarioRequestValidator()
    {
        RuleFor(x => x.Nombre).NotEmpty().WithErrorCode("REQUERIDO").MaximumLength(150).WithErrorCode("LONGITUD_MAXIMA");
        RuleFor(x => x.Rol).IsInEnum().WithErrorCode("VALOR_INVALIDO");
    }
}

public sealed class RestablecerPasswordRequestValidator : AbstractValidator<RestablecerPasswordRequest>
{
    public RestablecerPasswordRequestValidator() =>
        RuleFor(x => x.Password).Must(PoliticaPassword.Cumple).WithErrorCode("PASSWORD_DEBIL");
}

using System.Security.Cryptography;
using System.Text;
using CajaComun.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CajaComun.Infrastructure.Seguridad;

public sealed class OpcionesJwt
{
    public const string Emisor = "caja-comun";
    public const string Audiencia = "caja-comun-web";
    public const int LongitudMinimaSecreto = 32;

    public string Secreto { get; init; } = "";
    public int MinutosAcceso { get; init; } = 15;
    public int DiasRefresh { get; init; } = 7;

    public SymmetricSecurityKey Llave => new(Encoding.UTF8.GetBytes(Secreto));

    /// <summary>El secreto sale de JWT_SECRET (.env) o de Jwt:Secret; sin él la API no arranca.</summary>
    public static OpcionesJwt Desde(IConfiguration config)
    {
        var secreto = Environment.GetEnvironmentVariable("JWT_SECRET") is { Length: > 0 } v ? v : config["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secreto) || Encoding.UTF8.GetByteCount(secreto) < LongitudMinimaSecreto)
            throw new InvalidOperationException(
                $"Falta JWT_SECRET (o Jwt:Secret) de al menos {LongitudMinimaSecreto} caracteres. Defínelo en el archivo .env.");

        return new OpcionesJwt
        {
            Secreto = secreto,
            MinutosAcceso = int.TryParse(config["Jwt:MinutosAcceso"], out var m) && m > 0 ? m : 15,
            DiasRefresh = int.TryParse(config["Jwt:DiasRefresh"], out var d) && d > 0 ? d : 7
        };
    }
}

public sealed record RefreshNuevo(string Token, string Hash, DateTime Expira);

public sealed class TokenService(OpcionesJwt opciones)
{
    public const string ClaimRol = "role";
    public const string ClaimNombre = "name";

    public (string Token, int ExpiraEnSegundos) CrearAcceso(Usuario usuario, DateTime ahora)
    {
        var expira = ahora.AddMinutes(opciones.MinutosAcceso);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = OpcionesJwt.Emisor,
            Audience = OpcionesJwt.Audiencia,
            IssuedAt = ahora,
            NotBefore = ahora,
            Expires = expira,
            SigningCredentials = new SigningCredentials(opciones.Llave, SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = usuario.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = usuario.Correo,
                [ClaimNombre] = usuario.Nombre,
                [ClaimRol] = usuario.Rol.ToString()
            }
        };
        return (new JsonWebTokenHandler().CreateToken(descriptor), opciones.MinutosAcceso * 60);
    }

    /// <summary>Token opaco de 256 bits. En la base solo se guarda su hash SHA-256.</summary>
    public RefreshNuevo CrearRefresh(DateTime ahora)
    {
        var token = Base64Url(RandomNumberGenerator.GetBytes(32));
        return new RefreshNuevo(token, Hash(token), ahora.AddDays(opciones.DiasRefresh));
    }

    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

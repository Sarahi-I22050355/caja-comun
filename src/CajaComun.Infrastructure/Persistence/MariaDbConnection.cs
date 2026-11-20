using Microsoft.Extensions.Configuration;
using MySqlConnector;

namespace CajaComun.Infrastructure.Persistence;

/// <summary>
/// Arma el connection string desde las variables del .env (MARIADB_HOST, MARIADB_PORT, MARIADB_DATABASE,
/// MARIADB_USER, MARIADB_PASSWORD). Si no existen usa la sección MariaDb de la configuración; la contraseña
/// nunca se guarda en el repositorio.
/// </summary>
public static class MariaDbConnection
{
    public static string Build(IConfiguration? config = null)
    {
        string Valor(string clave, string variable, string? porDefecto = null) =>
            Environment.GetEnvironmentVariable(variable) is { Length: > 0 } v ? v
            : config?[$"MariaDb:{clave}"] is { Length: > 0 } c ? c
            : porDefecto ?? throw new InvalidOperationException(
                $"Falta {variable} (o MariaDb:{clave}). Define las variables del archivo .env.");

        return new MySqlConnectionStringBuilder
        {
            Server = Valor("Host", "MARIADB_HOST", "localhost"),
            Port = uint.Parse(Valor("Port", "MARIADB_PORT", "3307")),
            Database = Valor("Database", "MARIADB_DATABASE"),
            UserID = Valor("User", "MARIADB_USER"),
            Password = Valor("Password", "MARIADB_PASSWORD"),
            CharacterSet = "utf8mb4",
            MinimumPoolSize = 2,
            MaximumPoolSize = 50,
            ConnectionIdleTimeout = 180,
            DateTimeKind = MySqlDateTimeKind.Utc,
        }.ConnectionString;
    }
}

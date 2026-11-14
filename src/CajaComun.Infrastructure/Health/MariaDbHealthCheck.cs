using Microsoft.Extensions.Diagnostics.HealthChecks;
using MySqlConnector;

namespace CajaComun.Infrastructure.Health;

public sealed class MariaDbHealthCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var conn = new MySqlConnection(connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new MySqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("MariaDB no responde", ex);
        }
    }
}

using CajaComun.Infrastructure.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace CajaComun.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        var mariaDb = config.GetConnectionString("MariaDb")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:MariaDb");
        var redisCs = config.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("Falta ConnectionStrings:Redis");

        // abortConnect=false: la API arranca aunque Redis tarde; el health lo reporta.
        var redisOptions = ConfigurationOptions.Parse(redisCs);
        redisOptions.AbortOnConnectFail = false;
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(redisOptions));

        services.AddHealthChecks()
            .AddCheck("mariadb", new MariaDbHealthCheck(mariaDb), tags: ["ready"])
            .AddCheck<RedisHealthCheck>("redis", tags: ["ready"]);

        return services;
    }
}

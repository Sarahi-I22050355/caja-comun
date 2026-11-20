using System.Text.Json;
using CajaComun.Application.Comun;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CajaComun.Infrastructure.Cache;

/// <summary>
/// Caché en Redis con invalidación por versión: cada caja (y un contador global para las consultas de varias cajas) tiene
/// un número que forma parte de la llave. Al cambiar los datos se incrementa y las entradas anteriores quedan huérfanas
/// (vencen solas); no hace falta buscar ni borrar llaves. Si Redis no responde, se calcula sin caché.
/// </summary>
public sealed class CacheRedis(IServiceProvider servicios, ILogger<CacheRedis> log) : ICacheDatos
{
    // La conexión se pide en el primer uso: construir el caché no debe esperar a Redis.
    private readonly Lazy<IConnectionMultiplexer> _redis = new(() => servicios.GetRequiredService<IConnectionMultiplexer>());

    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromMilliseconds(750);

    private static string LlaveVersion(int? cajaId) => $"cache:v:{(cajaId is { } id ? id.ToString() : "global")}";

    public async Task<T> ObtenerOCrearAsync<T>(string clave, int? cajaId, TimeSpan vigencia, Func<Task<T>> crear)
    {
        string? llave = null;
        try
        {
            var db = _redis.Value.GetDatabase();
            var version = (long)await db.StringGetAsync(LlaveVersion(cajaId)).WaitAsync(EsperaMaxima);
            llave = $"cache:{clave}:{(cajaId is { } c ? c.ToString() : "global")}:v{version}";
            var guardado = await db.StringGetAsync(llave).WaitAsync(EsperaMaxima);
            if (guardado.HasValue && JsonSerializer.Deserialize<T>((string)guardado!) is { } valor) return valor;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException or JsonException)
        {
            log.LogWarning("Caché no disponible para {Clave}: {Mensaje}", clave, ex.Message);
            return await crear();
        }

        var nuevo = await crear();
        try
        {
            await _redis.Value.GetDatabase().StringSetAsync(llave!, JsonSerializer.Serialize(nuevo), vigencia).WaitAsync(EsperaMaxima);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            log.LogWarning("No se pudo guardar {Clave} en caché: {Mensaje}", clave, ex.Message);
        }
        return nuevo;
    }

    public async Task InvalidarAsync(int cajaId)
    {
        try
        {
            var db = _redis.Value.GetDatabase();
            await Task.WhenAll(
                db.StringIncrementAsync(LlaveVersion(cajaId)),
                db.StringIncrementAsync(LlaveVersion(null))).WaitAsync(EsperaMaxima);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            log.LogWarning("No se pudo invalidar el caché de la caja {Caja}: {Mensaje}", cajaId, ex.Message);
        }
    }
}

using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using CajaComun.Application.Comun;
using CajaComun.Application.Consultas;
using Polly;
using Polly.Retry;

namespace CajaComun.Infrastructure.Consultas;

public sealed class OpcionesTrino
{
    public string Url { get; set; } = "http://trino:8080";
    public string Usuario { get; set; } = "caja-comun";
    /// <summary>Tope de una consulta completa (envío + espera + lectura de resultados).</summary>
    public int TimeoutSegundos { get; set; } = 100;
}

public sealed record ResultadoTrino(IReadOnlyList<ColumnaConsulta> Columnas, IReadOnlyList<object?[]> Filas, long DuracionMs);

public interface ITrinoClient
{
    /// <summary>
    /// Ejecuta SQL con el protocolo REST de Trino. <paramref name="reintentar"/> solo debe ser verdadero para consultas
    /// de lectura: un INSERT que falla a medio camino no se puede repetir sin saber qué alcanzó a escribir.
    /// </summary>
    Task<ResultadoTrino> EjecutarAsync(string sql, bool reintentar = true, CancellationToken ct = default);
}

/// <summary>
/// Cliente del protocolo REST de Trino: POST /v1/statement con el SQL y luego GET de cada <c>nextUri</c> hasta que
/// ya no haya. Los fallos de red y los 502/503/504 se reintentan con Polly (espera creciente); el timeout total
/// acota la consulta completa.
/// </summary>
public sealed class TrinoClient : ITrinoClient
{
    private static readonly ResiliencePipeline<HttpResponseMessage> Reintentos = new ResiliencePipelineBuilder<HttpResponseMessage>()
        .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
        {
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            Delay = TimeSpan.FromMilliseconds(400),
            UseJitter = true,
            ShouldHandle = new PredicateBuilder<HttpResponseMessage>()
                .Handle<HttpRequestException>()
                .HandleResult(r => r.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout),
        })
        .Build();

    private readonly HttpClient _http;
    private readonly OpcionesTrino _opciones;

    public TrinoClient(HttpClient http, OpcionesTrino opciones)
    {
        _http = http;
        _opciones = opciones;
    }

    public async Task<ResultadoTrino> EjecutarAsync(string sql, bool reintentar = true, CancellationToken ct = default)
    {
        using var limite = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limite.CancelAfter(TimeSpan.FromSeconds(_opciones.TimeoutSegundos));
        var reloj = Stopwatch.StartNew();
        string? siguiente = null;
        try
        {
            var columnas = new List<ColumnaConsulta>();
            var filas = new List<object?[]>();

            using var primera = await EnviarAsync(HttpMethod.Post, $"{_opciones.Url.TrimEnd('/')}/v1/statement", sql, reintentar, limite.Token);
            var json = await LeerAsync(primera, limite.Token);
            siguiente = Procesar(json, columnas, filas);

            while (siguiente is not null)
            {
                using var resp = await EnviarAsync(HttpMethod.Get, siguiente, null, reintentar, limite.Token);
                json = await LeerAsync(resp, limite.Token);
                siguiente = Procesar(json, columnas, filas);
            }
            return new ResultadoTrino(columnas, filas, reloj.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            await CancelarAsync(siguiente);
            throw new ErrorNegocio(Codigos.FederacionNoDisponible,
                "La consulta tardó más del tiempo permitido y se canceló.", StatusCodes.Gateway);
        }
        catch (OperationCanceledException)
        {
            await CancelarAsync(siguiente);
            throw;
        }
        catch (HttpRequestException)
        {
            throw new ErrorNegocio(Codigos.FederacionNoDisponible,
                "No se pudo contactar a Trino. Levanta la federación con `docker compose --profile federation up -d`.",
                StatusCodes.ServicioNoDisponible);
        }
    }

    private async Task<HttpResponseMessage> EnviarAsync(HttpMethod metodo, string url, string? cuerpo, bool reintentar, CancellationToken ct)
    {
        async ValueTask<HttpResponseMessage> Una(CancellationToken t)
        {
            var peticion = new HttpRequestMessage(metodo, url);
            peticion.Headers.Add("X-Trino-User", _opciones.Usuario);
            peticion.Headers.Add("X-Trino-Source", "caja-comun-api");
            peticion.Headers.Add("X-Trino-Time-Zone", "America/Mexico_City");
            if (cuerpo is not null) peticion.Content = new StringContent(cuerpo, Encoding.UTF8, "text/plain");
            return await _http.SendAsync(peticion, HttpCompletionOption.ResponseHeadersRead, t);
        }

        return reintentar
            ? await Reintentos.ExecuteAsync(async t => await Una(t), ct)
            : await Una(ct);
    }

    private static async Task<JsonElement> LeerAsync(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout)
            throw new ErrorNegocio(Codigos.FederacionNoDisponible, "Trino no está listo todavía.", StatusCodes.ServicioNoDisponible);
        if (!resp.IsSuccessStatusCode)
            throw new ErrorNegocio(Codigos.ConsultaFederadaFallo, $"Trino respondió {(int)resp.StatusCode}.", StatusCodes.Gateway);
        await using var flujo = await resp.Content.ReadAsStreamAsync(ct);
        using var doc = await JsonDocument.ParseAsync(flujo, cancellationToken: ct);
        return doc.RootElement.Clone();
    }

    /// <summary>Acumula columnas y filas de una respuesta; devuelve el siguiente URI o null si terminó.</summary>
    private static string? Procesar(JsonElement json, List<ColumnaConsulta> columnas, List<object?[]> filas)
    {
        if (json.TryGetProperty("error", out var error))
        {
            var mensaje = error.TryGetProperty("message", out var m) ? m.GetString() : "Error desconocido";
            var nombre = error.TryGetProperty("errorName", out var n) ? n.GetString() : null;
            throw new ErrorNegocio(Codigos.ConsultaFederadaFallo,
                nombre is null ? $"Trino rechazó la consulta: {mensaje}" : $"Trino rechazó la consulta ({nombre}): {mensaje}",
                StatusCodes.Gateway);
        }

        if (columnas.Count == 0 && json.TryGetProperty("columns", out var cols))
            foreach (var c in cols.EnumerateArray())
                columnas.Add(new ColumnaConsulta(c.GetProperty("name").GetString()!, c.GetProperty("type").GetString()!));

        if (json.TryGetProperty("data", out var datos))
            foreach (var fila in datos.EnumerateArray())
                filas.Add(fila.EnumerateArray().Select(Convertir).ToArray());

        return json.TryGetProperty("nextUri", out var next) ? next.GetString() : null;
    }

    private static object? Convertir(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.Null => null,
        JsonValueKind.String => v.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        // El (object) evita que el operador ?: unifique ambos lados a decimal y los enteros salgan como 3213.00.
        JsonValueKind.Number => v.TryGetInt64(out var l) ? (object)l : v.GetDecimal(),
        _ => v.GetRawText(),
    };

    /// <summary>Avisa a Trino que ya no hace falta la consulta para que libere su memoria.</summary>
    private async Task CancelarAsync(string? uri)
    {
        if (uri is null) return;
        try
        {
            using var t = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var peticion = new HttpRequestMessage(HttpMethod.Delete, uri);
            peticion.Headers.Add("X-Trino-User", _opciones.Usuario);
            using var _ = await _http.SendAsync(peticion, t.Token);
        }
        catch
        {
            // Si no se pudo cancelar, Trino la cierra solo por su propio timeout.
        }
    }

    /// <summary>Códigos HTTP que usa este cliente (la capa de infraestructura no depende de Microsoft.AspNetCore.Http).</summary>
    private static class StatusCodes
    {
        public const int Gateway = 502;
        public const int ServicioNoDisponible = 503;
    }
}

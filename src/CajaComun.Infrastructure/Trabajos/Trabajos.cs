using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using CajaComun.Application.Comun;
using CajaComun.Application.Nomina;
using CajaComun.Application.Trabajos;
using CajaComun.Domain.Enums;
using CajaComun.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CajaComun.Infrastructure.Trabajos;

/// <summary>Estado de un trabajo tal como se guarda (sin el archivo, que va aparte).</summary>
public sealed record RegistroTrabajo(
    string Id, string Tipo, int UsuarioId, EstadoTrabajo Estado, DateTime Creado, DateTime? Inicio, DateTime? Fin,
    string? ResultadoJson, string? CodigoError, string? MensajeError, string? Archivo);

public interface IAlmacenTrabajos
{
    Task GuardarAsync(RegistroTrabajo registro);
    Task<RegistroTrabajo?> ObtenerAsync(string id);
    Task GuardarArchivoAsync(string id, ArchivoGenerado archivo);
    Task<ArchivoGenerado?> ObtenerArchivoAsync(string id);
}

/// <summary>Almacén en memoria: para pruebas y como respaldo si no se configura Redis.</summary>
public sealed class AlmacenTrabajosMemoria : IAlmacenTrabajos
{
    private readonly ConcurrentDictionary<string, RegistroTrabajo> _registros = new();
    private readonly ConcurrentDictionary<string, ArchivoGenerado> _archivos = new();

    public Task GuardarAsync(RegistroTrabajo registro) { _registros[registro.Id] = registro; return Task.CompletedTask; }
    public Task<RegistroTrabajo?> ObtenerAsync(string id) => Task.FromResult(_registros.GetValueOrDefault(id));
    public Task GuardarArchivoAsync(string id, ArchivoGenerado archivo) { _archivos[id] = archivo; return Task.CompletedTask; }
    public Task<ArchivoGenerado?> ObtenerArchivoAsync(string id) => Task.FromResult(_archivos.GetValueOrDefault(id));
}

/// <summary>El estado y el archivo de cada trabajo viven 24 horas en Redis, así que se pueden consultar entre peticiones.</summary>
public sealed class AlmacenTrabajosRedis(IServiceProvider servicios) : IAlmacenTrabajos
{
    // La conexión se pide en el primer uso: arrancar la API no debe esperar a Redis.
    private readonly Lazy<IConnectionMultiplexer> _redis = new(() => servicios.GetRequiredService<IConnectionMultiplexer>());

    private static readonly TimeSpan Vigencia = TimeSpan.FromHours(24);

    public Task GuardarAsync(RegistroTrabajo registro) =>
        _redis.Value.GetDatabase().StringSetAsync($"trabajo:{registro.Id}", JsonSerializer.Serialize(registro), Vigencia);

    public async Task<RegistroTrabajo?> ObtenerAsync(string id)
    {
        var valor = await _redis.Value.GetDatabase().StringGetAsync($"trabajo:{id}");
        return valor.HasValue ? JsonSerializer.Deserialize<RegistroTrabajo>((string)valor!) : null;
    }

    public Task GuardarArchivoAsync(string id, ArchivoGenerado archivo) =>
        _redis.Value.GetDatabase().StringSetAsync($"trabajo:{id}:archivo", JsonSerializer.Serialize(archivo), Vigencia);

    public async Task<ArchivoGenerado?> ObtenerArchivoAsync(string id)
    {
        var valor = await _redis.Value.GetDatabase().StringGetAsync($"trabajo:{id}:archivo");
        return valor.HasValue ? JsonSerializer.Deserialize<ArchivoGenerado>((string)valor!) : null;
    }
}

public sealed record ItemTrabajo(
    string Id, string Tipo, int UsuarioId, RolUsuario Rol, Func<IServiceProvider, CancellationToken, Task<ResultadoTrabajo>> Trabajo);

/// <summary>Cola en memoria (Channel) con el estado en el almacén; la procesa <see cref="ProcesadorTrabajos"/>.</summary>
public sealed class ColaTrabajos(IAlmacenTrabajos almacen, TimeProvider reloj) : IColaTrabajos
{
    private readonly Channel<ItemTrabajo> _canal = Channel.CreateBounded<ItemTrabajo>(
        new BoundedChannelOptions(200) { FullMode = BoundedChannelFullMode.Wait, SingleReader = false });

    public ChannelReader<ItemTrabajo> Lector => _canal.Reader;

    public async Task<TrabajoDto> EncolarAsync(
        string tipo, int usuarioId, RolUsuario rol,
        Func<IServiceProvider, CancellationToken, Task<ResultadoTrabajo>> trabajo, CancellationToken ct = default)
    {
        var registro = new RegistroTrabajo(
            Guid.NewGuid().ToString("N"), tipo, usuarioId, EstadoTrabajo.Pendiente, reloj.GetUtcNow().UtcDateTime,
            null, null, null, null, null, null);
        await almacen.GuardarAsync(registro);
        await _canal.Writer.WriteAsync(new ItemTrabajo(registro.Id, tipo, usuarioId, rol, trabajo), ct);
        return Mapear(registro);
    }

    public async Task<TrabajoDto?> ObtenerAsync(string id, int usuarioId, RolUsuario rol, CancellationToken ct = default)
    {
        var r = await almacen.ObtenerAsync(id);
        return r is null || !Visible(r, usuarioId, rol) ? null : Mapear(r);
    }

    public async Task<ArchivoGenerado?> ArchivoAsync(string id, int usuarioId, RolUsuario rol, CancellationToken ct = default)
    {
        var r = await almacen.ObtenerAsync(id);
        return r is null || !Visible(r, usuarioId, rol) ? null : await almacen.ObtenerArchivoAsync(id);
    }

    private static bool Visible(RegistroTrabajo r, int usuarioId, RolUsuario rol) =>
        r.UsuarioId == usuarioId || rol == RolUsuario.Administrador;

    internal static TrabajoDto Mapear(RegistroTrabajo r) => new(
        r.Id, r.Tipo, r.Estado, r.Creado, r.Inicio, r.Fin,
        r.ResultadoJson is null ? null : JsonSerializer.Deserialize<JsonElement>(r.ResultadoJson), r.CodigoError, r.MensajeError, r.Archivo);
}

/// <summary>Toma trabajos de la cola y los ejecuta (de dos en dos), cada uno en su propio ámbito y con el usuario que lo pidió.</summary>
public sealed class ProcesadorTrabajos(
    ColaTrabajos cola, IAlmacenTrabajos almacen, IServiceScopeFactory ambitos, TimeProvider reloj,
    ILogger<ProcesadorTrabajos> log) : BackgroundService
{
    private const int Concurrencia = 2;

    protected override Task ExecuteAsync(CancellationToken ct) =>
        Task.WhenAll(Enumerable.Range(0, Concurrencia).Select(_ => Trabajar(ct)));

    private async Task Trabajar(CancellationToken ct)
    {
        try
        {
            await foreach (var item in cola.Lector.ReadAllAsync(ct)) await EjecutarAsync(item, ct);
        }
        catch (OperationCanceledException) { }
    }

    private async Task EjecutarAsync(ItemTrabajo item, CancellationToken ct)
    {
        var actual = await almacen.ObtenerAsync(item.Id);
        if (actual is null) return;
        var en = actual with { Estado = EstadoTrabajo.EnCurso, Inicio = reloj.GetUtcNow().UtcDateTime };
        await almacen.GuardarAsync(en);

        try
        {
            await using var ambito = ambitos.CreateAsyncScope();
            ambito.ServiceProvider.GetRequiredService<ContextoTrabajo>().Fijar(item.UsuarioId, item.Rol);
            var resultado = await item.Trabajo(ambito.ServiceProvider, ct);
            string? nombreArchivo = null;
            if (resultado.Archivo is { } archivo)
            {
                await almacen.GuardarArchivoAsync(item.Id, archivo);
                nombreArchivo = archivo.NombreArchivo;
            }
            await almacen.GuardarAsync(en with
            {
                Estado = EstadoTrabajo.Exito, Fin = reloj.GetUtcNow().UtcDateTime, Archivo = nombreArchivo,
                ResultadoJson = resultado.Datos is null ? null : JsonSerializer.Serialize(resultado.Datos, NombreEnumDb.Opciones)
            });
        }
        catch (ErrorNegocio ex)
        {
            await almacen.GuardarAsync(en with
            {
                Estado = EstadoTrabajo.Error, Fin = reloj.GetUtcNow().UtcDateTime, CodigoError = ex.Codigo, MensajeError = ex.Message
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogError(ex, "Falló el trabajo {Id} ({Tipo})", item.Id, item.Tipo);
            await almacen.GuardarAsync(en with
            {
                Estado = EstadoTrabajo.Error, Fin = reloj.GetUtcNow().UtcDateTime, CodigoError = Codigos.ErrorInterno,
                MensajeError = "Ocurrió un error inesperado."
            });
        }
    }
}

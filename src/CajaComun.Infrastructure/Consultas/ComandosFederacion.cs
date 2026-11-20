using System.Globalization;
using CajaComun.Application.Consultas;
using CajaComun.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CajaComun.Infrastructure.Consultas;

/// <summary>
/// Comandos de línea de comandos de la Parte 2 (corren y terminan, sin levantar el servidor):
/// <c>cargar-federacion</c> copia las cajas a sus motores; <c>verificar-federacion [--anio N]</c> ejecuta las cinco consultas
/// federadas y las compara con las directas sobre MariaDB, y escribe el resultado en Markdown.
/// </summary>
public static class ComandosFederacion
{
    public static bool Reconoce(string? comando) => comando is "cargar-federacion" or "verificar-federacion";

    public static async Task<int> EjecutarAsync(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        config["Tareas:Habilitadas"] = "false";

        var servicios = new ServiceCollection();
        servicios.AddLogging();
        servicios.AddInfrastructure(config);
        await using var raiz = servicios.BuildServiceProvider();
        await using var alcance = raiz.CreateAsyncScope();

        try
        {
            return args[0] == "cargar-federacion"
                ? await CargarAsync(alcance.ServiceProvider)
                : await VerificarAsync(alcance.ServiceProvider, args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> CargarAsync(IServiceProvider sp)
    {
        Console.WriteLine("Cargando las cajas a sus motores (Trino: MariaDB -> PostgreSQL / ClickHouse)...");
        foreach (var linea in await sp.GetRequiredService<ICargaFederacion>().CargarAsync())
            Console.WriteLine(linea);
        Console.WriteLine("Carga terminada.");
        return 0;
    }

    private static async Task<int> VerificarAsync(IServiceProvider sp, string[] args)
    {
        var anio = Valor(args, "--anio") is { } v ? int.Parse(v, CultureInfo.InvariantCulture) : (int?)null;
        var motor = sp.GetRequiredService<MotorConsultas>();

        var estado = await motor.EstadoAsync();
        if (!estado.Disponible)
        {
            Console.Error.WriteLine($"La federación no responde ({estado.Motivo}). Levántala con `docker compose --profile federation up -d`.");
            return 2;
        }

        Console.WriteLine("| Motor | Cajas | Ahorradores | Movimientos |");
        Console.WriteLine("|---|---|---:|---:|");
        foreach (var m in estado.Motores)
            Console.WriteLine($"| {m.Motor} | {m.Cajas} | {m.Ahorradores} | {m.Movimientos} |");
        Console.WriteLine();
        Console.WriteLine("| Consulta | Año | Filas federada | Filas MariaDB | Resultado | Federada (ms) | MariaDB (ms) |");
        Console.WriteLine("|---|---|---:|---:|---|---:|---:|");

        // Sin --anio, las consultas con año se verifican para cada año que tenga ciclo.
        List<int> anios = anio is { } unico
            ? new List<int> { unico }
            : await sp.GetRequiredService<CajaComunDbContext>().Ciclos.AsNoTracking().Select(c => c.Anio).Distinct().OrderBy(a => a).ToListAsync();
        if (anios.Count == 0) anios.Add(DateTime.UtcNow.Year);

        var fallos = 0;
        foreach (var def in CatalogoConsultas.Definiciones)
        {
            foreach (var a in def.UsaAnio ? anios.Select(x => (int?)x) : new int?[] { null })
            {
                var e = await motor.VerificarEquivalenciaAsync(def.Numero, a);
                if (!e.Coincide) fallos++;
                var resultado = e.Coincide ? "idéntico" : $"{e.Diferencias} diferencias ({e.Ejemplo})";
                Console.WriteLine($"| Q{def.Numero} | {(a is { } y ? y.ToString(CultureInfo.InvariantCulture) : "—")} | {e.FilasFederadas} | {e.FilasDirectas} | {resultado} | {e.DuracionFederadaMs} | {e.DuracionDirectaMs} |");
            }
        }
        return fallos == 0 ? 0 : 3;
    }

    private static string? Valor(string[] args, string nombre)
    {
        var i = Array.IndexOf(args, nombre);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}

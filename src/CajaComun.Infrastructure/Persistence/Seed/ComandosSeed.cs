using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace CajaComun.Infrastructure.Persistence.Seed;

/// <summary>
/// Comandos de línea de comandos de la API: <c>seed [--perfil demo|full] [--reiniciar]</c>, <c>verificar-seed</c> y <c>migrar</c>.
/// Se ejecutan sin levantar el servidor web.
/// </summary>
public static class ComandosSeed
{
    public static bool Reconoce(string? comando) => comando is "seed" or "verificar-seed" or "migrar";

    public static async Task<int> EjecutarAsync(string[] args)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var opciones = new DbContextOptionsBuilder<CajaComunDbContext>()
            .UseMySql(MariaDbConnection.Build(config), new MariaDbServerVersion(new Version(11, 4, 0)))
            .Options;
        await using var db = new CajaComunDbContext(opciones);

        try
        {
            return args[0] switch
            {
                "seed" => await SembrarAsync(db, args),
                "migrar" => await MigrarAsync(db),
                _ => await VerificarAsync(db)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (ex.InnerException is not null) Console.Error.WriteLine(ex.InnerException.Message);
            return 1;
        }
    }

    private static async Task<int> MigrarAsync(CajaComunDbContext db)
    {
        var pendientes = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pendientes.Count == 0)
        {
            Console.WriteLine("La base de datos ya está al día.");
            return 0;
        }
        foreach (var m in pendientes) Console.WriteLine($"Aplicando {m}...");
        await db.Database.MigrateAsync();
        Console.WriteLine("Migraciones aplicadas.");
        return 0;
    }

    private static async Task<int> SembrarAsync(CajaComunDbContext db, string[] args)
    {
        var nombrePerfil = ValorDe(args, "--perfil") ?? "demo";
        var perfil = PerfilSeed.Buscar(nombrePerfil);
        if (perfil is null)
        {
            Console.Error.WriteLine($"Perfil desconocido: {nombrePerfil} (usa demo o full).");
            return 2;
        }

        var migrador = db.GetService<IMigrator>();
        if (args.Contains("--reiniciar"))
        {
            Console.WriteLine("Reiniciando: se eliminan todas las tablas y objetos...");
            await migrador.MigrateAsync("0");
        }
        Console.WriteLine("Aplicando migraciones...");
        await db.Database.MigrateAsync();

        if (await db.Empresas.AnyAsync())
        {
            Console.Error.WriteLine("La base de datos ya tiene datos. Usa --reiniciar para volver a sembrarla.");
            return 3;
        }

        var cronometro = Stopwatch.StartNew();
        var asignadas = new List<string>();
        string Contrasena(string variable)
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } v) return v;
            var nueva = ContrasenaAleatoria();
            asignadas.Add($"  {variable} = {nueva}");
            return nueva;
        }

        Console.WriteLine($"Sembrando perfil {perfil.Nombre}...");
        await new Sembrador(db, perfil, Contrasena).EjecutarAsync(Console.Out);
        Console.WriteLine($"Datos listos en {cronometro.Elapsed.TotalSeconds:N1} s. Verificando...");

        var codigo = await VerificarAsync(db);
        if (asignadas.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Contraseñas de demostración asignadas (solo se muestran ahora; defínelas en .env para fijarlas):");
            asignadas.ForEach(Console.WriteLine);
        }
        return codigo;
    }

    private static async Task<int> VerificarAsync(CajaComunDbContext db)
    {
        var resultados = await new VerificadorSeed(db).EjecutarAsync();
        foreach (var r in resultados)
            Console.WriteLine($"  [{(r.Correcto ? "OK" : "FALLA")}] {r.Nombre} - {r.Detalle}");
        var fallas = resultados.Count(r => !r.Correcto);
        Console.WriteLine(fallas == 0
            ? $"Verificación completa: {resultados.Count} comprobaciones correctas."
            : $"Verificación con errores: {fallas} de {resultados.Count} fallaron.");
        return fallas == 0 ? 0 : 4;
    }

    private static string? ValorDe(string[] args, string nombre)
    {
        var i = Array.IndexOf(args, nombre);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static string ContrasenaAleatoria()
    {
        const string alfabeto = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
        var bytes = RandomNumberGenerator.GetBytes(16);
        return new string(bytes.Select(b => alfabeto[b % alfabeto.Length]).ToArray());
    }
}

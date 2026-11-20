using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace CajaComun.Infrastructure.Persistence;

/// <summary>Solo para las herramientas de migraciones (dotnet ef); no conecta a la BD al generar.</summary>
public class CajaComunDbContextFactory : IDesignTimeDbContextFactory<CajaComunDbContext>
{
    public CajaComunDbContext CreateDbContext(string[] args)
    {
        // Al generar migraciones no se abre conexión, así que basta un connection string sin credenciales.
        var cs = Environment.GetEnvironmentVariable("MARIADB_PASSWORD") is null
            ? "Server=localhost;Database=caja_comun;User ID=ef"
            : MariaDbConnection.Build();
        var opciones = new DbContextOptionsBuilder<CajaComunDbContext>()
            .UseMySql(cs, new MariaDbServerVersion(new Version(11, 4, 0)))
            .Options;
        return new CajaComunDbContext(opciones);
    }
}

using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CajaComun.Infrastructure.Persistence;

/// <summary>
/// Consultas de Dapper sobre la conexión (y la transacción) del DbContext: lo que se lee dentro de una
/// transacción de EF ve sus propios cambios y comparte sus candados.
/// </summary>
public static class DbContextDapper
{
    public static async Task<IReadOnlyList<T>> ConsultarAsync<T>(
        this DbContext db, string sql, object? parametros = null, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var tx = db.Database.CurrentTransaction?.GetDbTransaction();
            var filas = await db.Database.GetDbConnection()
                .QueryAsync<T>(new CommandDefinition(sql, parametros, tx, cancellationToken: ct));
            return filas.AsList();
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    public static async Task<T> EscalarAsync<T>(
        this DbContext db, string sql, object? parametros = null, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var tx = db.Database.CurrentTransaction?.GetDbTransaction();
            return (await db.Database.GetDbConnection()
                .ExecuteScalarAsync<T>(new CommandDefinition(sql, parametros, tx, cancellationToken: ct)))!;
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    public static async Task<int> EjecutarAsync(
        this DbContext db, string sql, object? parametros = null, CancellationToken ct = default)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            var tx = db.Database.CurrentTransaction?.GetDbTransaction();
            return await db.Database.GetDbConnection()
                .ExecuteAsync(new CommandDefinition(sql, parametros, tx, cancellationToken: ct));
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }
}

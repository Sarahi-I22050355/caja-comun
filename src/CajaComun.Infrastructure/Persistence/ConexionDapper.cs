using System.Data;
using Dapper;
using MySqlConnector;

namespace CajaComun.Infrastructure.Persistence;

/// <summary>Conexiones para las consultas de lectura con Dapper (vistas y listados con proyección a DTO).</summary>
public interface IFabricaConexion
{
    Task<MySqlConnection> AbrirAsync(CancellationToken ct = default);
}

public sealed class FabricaConexion : IFabricaConexion
{
    private readonly string _cadena;

    public FabricaConexion(string cadena)
    {
        _cadena = cadena;
        ManejadoresDapper.Registrar();
    }

    public async Task<MySqlConnection> AbrirAsync(CancellationToken ct = default)
    {
        var conexion = new MySqlConnection(_cadena);
        await conexion.OpenAsync(ct);
        return conexion;
    }
}

internal static class ManejadoresDapper
{
    private static int _registrado;

    public static void Registrar()
    {
        if (Interlocked.Exchange(ref _registrado, 1) == 1) return;
        // Los procedimientos almacenados devuelven columnas en snake_case (prestamos_procesados): se asocian a PascalCase.
        DefaultTypeMap.MatchNamesWithUnderscores = true;
        SqlMapper.AddTypeHandler(new ManejadorDateOnly());
        SqlMapper.AddTypeHandler(new ManejadorDateOnlyNulo());
    }

    private sealed class ManejadorDateOnly : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value) => value switch
        {
            DateOnly d => d,
            DateTime t => DateOnly.FromDateTime(t),
            _ => DateOnly.Parse(value.ToString()!)
        };

        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value.ToDateTime(TimeOnly.MinValue);
        }
    }

    private sealed class ManejadorDateOnlyNulo : SqlMapper.TypeHandler<DateOnly?>
    {
        public override DateOnly? Parse(object value) => value is null or DBNull
            ? null
            : value switch { DateOnly d => d, DateTime t => DateOnly.FromDateTime(t), _ => DateOnly.Parse(value.ToString()!) };

        public override void SetValue(IDbDataParameter parameter, DateOnly? value)
        {
            parameter.DbType = DbType.Date;
            parameter.Value = value is { } d ? d.ToDateTime(TimeOnly.MinValue) : DBNull.Value;
        }
    }
}

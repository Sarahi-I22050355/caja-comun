using System.Globalization;
using CajaComun.Application.Consultas;

namespace CajaComun.Infrastructure.Consultas;

/// <summary>
/// Proceso de carga: copia de MariaDB (la fuente) a PostgreSQL y a ClickHouse los datos de las cajas que viven allí.
/// Usa el propio Trino (INSERT ... SELECT entre catálogos), así no hace falta ningún otro cliente de base de datos.
/// Se puede repetir: vacía las tablas de destino y las vuelve a llenar. No copia RFC, CLABE ni sueldos.
/// </summary>
public sealed class CargaFederacion(ITrinoClient trino) : ICargaFederacion
{
    private sealed record Destino(string Motor, string Esquema, string Prefijo);

    private static readonly Destino[] Destinos =
    [
        new(DistribucionMotores.PostgreSql, "postgresql.public", "ACE"),
        new(DistribucionMotores.ClickHouse, "clickhouse.bravo", "TDB"),
    ];

    private const string Origen = "mariadb.caja_comun";

    // (tabla, columnas, condición sobre las filas de la caja)
    private static readonly (string Tabla, string Columnas, string Filtro)[] Tablas =
    [
        ("ahorrador", "id, caja_id, num_empleado, nombre, apellidos, fecha_nacimiento, fecha_ingreso, fecha_baja, estado, departamento", "caja_id IN ({cajas})"),
        ("movimiento", "id, caja_id, ahorrador_id, prestamo_id, fecha, tipo, monto", "caja_id IN ({cajas})"),
        ("prestamo", "id, folio, caja_id, ahorrador_id, monto, plazo_quincenas, tasa_mensual, estado, fecha_solicitud, fecha_autorizacion", "caja_id IN ({cajas})"),
        ("ciclo", "id, caja_id, anio, estado, interes_total", "caja_id IN ({cajas})"),
        ("ciclo_distribucion", "ciclo_id, ahorrador_id, interes_asignado, ahorro_liquidado, ahorro_retenido",
            $"ciclo_id IN (SELECT id FROM {Origen}.ciclo WHERE caja_id IN ({{cajas}}))"),
    ];

    public async Task<IReadOnlyList<string>> CargarAsync(CancellationToken ct = default)
    {
        var bitacora = new List<string>();
        await trino.EjecutarAsync("SELECT 1", ct: ct); // falla pronto y con mensaje claro si Trino no está arriba

        foreach (var destino in Destinos)
        {
            var cajas = $"SELECT id FROM {Origen}.caja WHERE prefijo = '{destino.Prefijo}'";
            foreach (var (tabla, columnas, filtro) in Tablas)
            {
                var objetivo = $"{destino.Esquema}.{tabla}";
                await Vaciar(objetivo, ct);
                // ClickHouse guarda la fecha de nacimiento como texto (ver infra/federacion/clickhouse/init.sql).
                var seleccion = destino.Motor == DistribucionMotores.ClickHouse && tabla == "ahorrador"
                    ? columnas.Replace("fecha_nacimiento", "CAST(fecha_nacimiento AS varchar)")
                    : columnas;
                var r = await trino.EjecutarAsync(
                    $"INSERT INTO {objetivo} ({columnas})\nSELECT {seleccion} FROM {Origen}.{tabla} WHERE {filtro.Replace("{cajas}", cajas)}",
                    reintentar: false, ct);
                var filas = r.Filas.Count > 0 ? Convert.ToInt64(r.Filas[0][0], CultureInfo.InvariantCulture) : 0;
                bitacora.Add($"{destino.Motor,-10} {tabla,-20} {filas,8} filas");
            }
        }
        return bitacora;
    }

    /// <summary>TRUNCATE si el conector lo soporta; si no, DELETE sin condición (se empuja al motor como un solo DELETE).</summary>
    private async Task Vaciar(string tabla, CancellationToken ct)
    {
        try
        {
            await trino.EjecutarAsync($"TRUNCATE TABLE {tabla}", reintentar: false, ct);
        }
        catch (CajaComun.Application.Comun.ErrorNegocio e) when (e.Codigo == CajaComun.Application.Comun.Codigos.ConsultaFederadaFallo)
        {
            await trino.EjecutarAsync($"DELETE FROM {tabla}", reintentar: false, ct);
        }
    }
}

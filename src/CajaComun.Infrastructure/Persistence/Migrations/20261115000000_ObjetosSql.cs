using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CajaComun.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Crea las vistas, la función y los procedimientos de db/objetos. Los .sql viajan como recursos
    /// embebidos para que el esquema completo se arme solo con las migraciones. Si un objeto cambia,
    /// se agrega una migración nueva que lo vuelva a crear (esta ya pudo haberse aplicado).
    /// </summary>
    [DbContext(typeof(CajaComunDbContext))]
    [Migration("20261115000000_ObjetosSql")]
    public class ObjetosSql : Migration
    {
        private static readonly (string Archivo, string Tipo, string Nombre)[] Objetos =
        [
            ("01_fn_saldo_ahorro", "FUNCTION", "fn_saldo_ahorro"),
            ("02_v_saldo_ahorrador", "VIEW", "v_saldo_ahorrador"),
            ("03_v_prestamo_resumen", "VIEW", "v_prestamo_resumen"),
            ("04_v_estado_cuenta", "VIEW", "v_estado_cuenta"),
            ("05_v_descuento_periodo", "VIEW", "v_descuento_periodo"),
            ("06_sp_calcular_interes_devengado", "PROCEDURE", "sp_calcular_interes_devengado"),
            ("07_sp_saldo_dias_ciclo", "PROCEDURE", "sp_saldo_dias_ciclo"),
        ];

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (archivo, tipo, nombre) in Objetos)
            {
                migrationBuilder.Sql($"DROP {tipo} IF EXISTS {nombre}");
                migrationBuilder.Sql(Leer(archivo));
            }
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (_, tipo, nombre) in Objetos.Reverse())
                migrationBuilder.Sql($"DROP {tipo} IF EXISTS {nombre}");
        }

        internal static string Leer(string archivo)
        {
            var recurso = $"objetos/{archivo}.sql";
            using var flujo = Assembly.GetExecutingAssembly().GetManifestResourceStream(recurso)
                ?? throw new InvalidOperationException($"No se encontró el recurso {recurso}.");
            using var lector = new StreamReader(flujo);
            return lector.ReadToEnd();
        }
    }
}

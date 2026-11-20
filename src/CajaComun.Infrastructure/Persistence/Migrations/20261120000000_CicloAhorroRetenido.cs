using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CajaComun.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Al cerrar un ciclo, quien tiene un préstamo vigente conserva parte de su ahorro como garantía. Se guarda cuánto
    /// para poder mostrarlo en la distribución; en los ciclos ya cerrados no hubo retenciones. Además se amplía el
    /// formato de los layouts generados (GENERICO_CSV y BANORTE_SIMPLIFICADO no caben en 10 caracteres).
    /// </summary>
    [DbContext(typeof(CajaComunDbContext))]
    [Migration("20261120000000_CicloAhorroRetenido")]
    public class CicloAhorroRetenido : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ahorro_retenido",
                table: "ciclo_distribucion",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<string>(
                name: "formato",
                table: "layout_generado",
                type: "varchar(30)",
                maxLength: 30,
                nullable: false,
                collation: "utf8mb4_unicode_ci",
                oldClrType: typeof(string),
                oldType: "varchar(10)",
                oldMaxLength: 10);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Los formatos nuevos no caben en 10 caracteres: se recortan para poder revertir (el nombre del archivo conserva el dato).
            migrationBuilder.Sql("UPDATE layout_generado SET formato = LEFT(formato, 10)");
            migrationBuilder.AlterColumn<string>(
                name: "formato",
                table: "layout_generado",
                type: "varchar(10)",
                maxLength: 10,
                nullable: false,
                collation: "utf8mb4_unicode_ci",
                oldClrType: typeof(string),
                oldType: "varchar(30)",
                oldMaxLength: 30);

            migrationBuilder.DropColumn(name: "ahorro_retenido", table: "ciclo_distribucion");
        }
    }
}

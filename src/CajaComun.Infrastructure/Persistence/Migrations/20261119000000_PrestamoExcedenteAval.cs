using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CajaComun.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Guarda cuánto del préstamo respalda el aval. Sin este dato no se puede saber cuánto ahorro del aval ya está
    /// comprometido cuando se le pide respaldar otro préstamo. Los préstamos con pagaré que ya existían se
    /// calculan con el ahorro al día del desembolso (los datos de demostración no tienen préstamos traslapados).
    /// </summary>
    [DbContext(typeof(CajaComunDbContext))]
    [Migration("20261119000000_PrestamoExcedenteAval")]
    public class PrestamoExcedenteAval : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "excedente_aval",
                table: "prestamo",
                type: "decimal(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE prestamo p
                  JOIN movimiento m ON m.prestamo_id = p.id AND m.tipo = 'DESEMBOLSO_PRESTAMO'
                   SET p.excedente_aval = LEAST(p.monto, GREATEST(0, p.monto - ROUND(0.8 * fn_saldo_ahorro(p.ahorrador_id, m.fecha), 2)))
                 WHERE p.garantia = 'PAGARE_AVAL'
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_prestamo_excedente",
                table: "prestamo",
                sql: "excedente_aval >= 0 AND excedente_aval <= monto");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(name: "ck_prestamo_excedente", table: "prestamo");
            migrationBuilder.DropColumn(name: "excedente_aval", table: "prestamo");
        }
    }
}

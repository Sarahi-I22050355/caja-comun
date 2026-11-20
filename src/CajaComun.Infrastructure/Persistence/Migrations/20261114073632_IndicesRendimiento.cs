using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CajaComun.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IndicesRendimiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Primero se crean los índices nuevos: MariaDB no deja soltar un índice que respalda una llave foránea.
            migrationBuilder.CreateIndex(
                name: "ix_prestamo_amortizacion_periodo_id_estado",
                table: "prestamo_amortizacion",
                columns: new[] { "periodo_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_prestamo_caja_id_estado",
                table: "prestamo",
                columns: new[] { "caja_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_ahorrador_id_fecha",
                table: "movimiento",
                columns: new[] { "ahorrador_id", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_caja_id_periodo_id",
                table: "movimiento",
                columns: new[] { "caja_id", "periodo_id" });

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_tipo_fecha",
                table: "movimiento",
                columns: new[] { "tipo", "fecha" });

            migrationBuilder.CreateIndex(
                name: "ix_ahorrador_caja_id_apellidos_nombre",
                table: "ahorrador",
                columns: new[] { "caja_id", "apellidos", "nombre" });

            migrationBuilder.CreateIndex(
                name: "ix_ahorrador_caja_id_estado",
                table: "ahorrador",
                columns: new[] { "caja_id", "estado" });

            migrationBuilder.DropIndex(
                name: "ix_prestamo_amortizacion_periodo_id",
                table: "prestamo_amortizacion");

            migrationBuilder.DropIndex(
                name: "ix_prestamo_caja_id",
                table: "prestamo");

            migrationBuilder.DropIndex(
                name: "ix_movimiento_ahorrador_id",
                table: "movimiento");

            migrationBuilder.DropIndex(
                name: "ix_movimiento_caja_id",
                table: "movimiento");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_prestamo_amortizacion_periodo_id",
                table: "prestamo_amortizacion",
                column: "periodo_id");

            migrationBuilder.CreateIndex(
                name: "ix_prestamo_caja_id",
                table: "prestamo",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_ahorrador_id",
                table: "movimiento",
                column: "ahorrador_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_caja_id",
                table: "movimiento",
                column: "caja_id");

            migrationBuilder.DropIndex(
                name: "ix_prestamo_amortizacion_periodo_id_estado",
                table: "prestamo_amortizacion");

            migrationBuilder.DropIndex(
                name: "ix_prestamo_caja_id_estado",
                table: "prestamo");

            migrationBuilder.DropIndex(
                name: "ix_movimiento_ahorrador_id_fecha",
                table: "movimiento");

            migrationBuilder.DropIndex(
                name: "ix_movimiento_caja_id_periodo_id",
                table: "movimiento");

            migrationBuilder.DropIndex(
                name: "ix_movimiento_tipo_fecha",
                table: "movimiento");

            migrationBuilder.DropIndex(
                name: "ix_ahorrador_caja_id_apellidos_nombre",
                table: "ahorrador");

            migrationBuilder.DropIndex(
                name: "ix_ahorrador_caja_id_estado",
                table: "ahorrador");
        }
    }
}

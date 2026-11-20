using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CajaComun.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// v_saldo_ahorrador se rehízo sin tabla derivada: unida a la lista de ahorradores (2,200 renglones) la
    /// versión anterior tardaba unos 4.7 s porque MariaDB recalculaba la derivada por cada ahorrador; ahora
    /// tarda unos 0.2 s con el mismo resultado. Vuelve a crear la vista desde db/objetos.
    /// </summary>
    [DbContext(typeof(CajaComunDbContext))]
    [Migration("20261116000000_VistaSaldoAhorradorSinDerivada")]
    public class VistaSaldoAhorradorSinDerivada : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP VIEW IF EXISTS v_saldo_ahorrador");
            migrationBuilder.Sql(ObjetosSql.Leer("02_v_saldo_ahorrador"));
        }

        // Ambas definiciones dan el mismo resultado; revertir solo cambiaría el costo de la consulta.
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}

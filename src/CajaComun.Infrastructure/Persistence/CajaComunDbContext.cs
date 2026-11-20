using System.Text.RegularExpressions;
using CajaComun.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;

namespace CajaComun.Infrastructure.Persistence;

public partial class CajaComunDbContext(DbContextOptions<CajaComunDbContext> options) : DbContext(options)
{
    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Caja> Cajas => Set<Caja>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<UsuarioCaja> UsuarioCajas => Set<UsuarioCaja>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Ahorrador> Ahorradores => Set<Ahorrador>();
    public DbSet<CuotaAhorro> CuotasAhorro => Set<CuotaAhorro>();
    public DbSet<Periodo> Periodos => Set<Periodo>();
    public DbSet<Movimiento> Movimientos => Set<Movimiento>();
    public DbSet<Prestamo> Prestamos => Set<Prestamo>();
    public DbSet<PrestamoAmortizacion> Amortizaciones => Set<PrestamoAmortizacion>();
    public DbSet<Incidencia> Incidencias => Set<Incidencia>();
    public DbSet<Importacion> Importaciones => Set<Importacion>();
    public DbSet<ImportacionDetalle> ImportacionDetalles => Set<ImportacionDetalle>();
    public DbSet<LayoutGenerado> Layouts => Set<LayoutGenerado>();
    public DbSet<Ciclo> Ciclos => Set<Ciclo>();
    public DbSet<CicloDistribucion> CicloDistribuciones => Set<CicloDistribucion>();
    public DbSet<InteresDevengado> InteresesDevengados => Set<InteresDevengado>();
    public DbSet<TareaProgramada> Tareas => Set<TareaProgramada>();
    public DbSet<TareaEjecucion> TareaEjecuciones => Set<TareaEjecucion>();

    protected override void ConfigureConventions(ModelConfigurationBuilder cfg)
    {
        cfg.Properties<decimal>().HavePrecision(14, 2);   // dinero
        cfg.Properties<string>().HaveMaxLength(200);
    }

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.UseCollation("utf8mb4_unicode_ci");
        mb.HasCharSet(CharSet.Utf8Mb4);

        mb.Entity<Empresa>(e =>
        {
            e.HasIndex(x => x.Nombre).IsUnique();
        });

        mb.Entity<Caja>(e =>
        {
            e.HasOne(x => x.Empresa).WithMany(x => x.Cajas).HasForeignKey(x => x.EmpresaId);
            e.HasIndex(x => new { x.EmpresaId, x.Nombre }).IsUnique();
            e.Property(x => x.Prefijo).HasMaxLength(5);
            e.Property(x => x.TasaInteresMensual).HasPrecision(5, 2);
            e.Property(x => x.FrecuenciaNomina).HasEnumColumn();
            e.ToTable(t => t.HasCheckConstraint("ck_caja_tasa", "tasa_interes_mensual BETWEEN 1.00 AND 2.00"));
        });

        mb.Entity<Usuario>(e =>
        {
            e.HasIndex(x => x.Correo).IsUnique();
            e.Property(x => x.HashPassword).HasMaxLength(100);
            e.Property(x => x.Rol).HasEnumColumn();
        });

        mb.Entity<UsuarioCaja>(e =>
        {
            e.HasKey(x => new { x.UsuarioId, x.CajaId });
            e.HasOne(x => x.Usuario).WithMany(x => x.Cajas).HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Caja).WithMany().HasForeignKey(x => x.CajaId);
        });

        mb.Entity<RefreshToken>(e =>
        {
            e.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
        });

        mb.Entity<Ahorrador>(e =>
        {
            e.HasOne(x => x.Caja).WithMany().HasForeignKey(x => x.CajaId);
            e.HasIndex(x => new { x.CajaId, x.NumEmpleado }).IsUnique();
            e.Property(x => x.NumEmpleado).HasMaxLength(20);
            e.Property(x => x.Rfc).HasMaxLength(13);
            e.Property(x => x.Clabe).HasColumnType("char(18)");
            e.HasIndex(x => new { x.CajaId, x.Estado });
            e.HasIndex(x => new { x.CajaId, x.Apellidos, x.Nombre });
            e.Property(x => x.Estado).HasEnumColumn();
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_ahorrador_sueldo", "sueldo_quincenal >= 0");
                t.HasCheckConstraint("ck_ahorrador_clabe", "clabe REGEXP '^[0-9]{18}$'");
            });
        });

        mb.Entity<CuotaAhorro>(e =>
        {
            e.HasOne(x => x.Ahorrador).WithMany().HasForeignKey(x => x.AhorradorId);
            e.HasIndex(x => new { x.AhorradorId, x.VigenteDesde }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_cuota_montos", "cuota_ordinaria >= 0 AND cuota_voluntaria >= 0"));
        });

        mb.Entity<Periodo>(e =>
        {
            e.HasOne(x => x.Caja).WithMany().HasForeignKey(x => x.CajaId);
            e.HasIndex(x => new { x.CajaId, x.Anio, x.Numero }).IsUnique();
            e.Property(x => x.Estado).HasEnumColumn();
            e.ToTable(t => t.HasCheckConstraint("ck_periodo_fechas", "fecha_fin >= fecha_inicio AND numero BETWEEN 1 AND 53"));
        });

        mb.Entity<Movimiento>(e =>
        {
            e.HasOne<Caja>().WithMany().HasForeignKey(x => x.CajaId);
            e.HasOne<Ahorrador>().WithMany().HasForeignKey(x => x.AhorradorId);
            e.HasOne<Periodo>().WithMany().HasForeignKey(x => x.PeriodoId);
            e.HasOne<Prestamo>().WithMany().HasForeignKey(x => x.PrestamoId);
            e.Property(x => x.Tipo).HasEnumColumn();
            e.Property(x => x.Referencia).HasMaxLength(60);
            // Índices de rendimiento (ver docs/indices.md)
            e.HasIndex(x => new { x.AhorradorId, x.Fecha });
            e.HasIndex(x => new { x.CajaId, x.PeriodoId });
            e.HasIndex(x => new { x.Tipo, x.Fecha });
            e.ToTable(t => t.HasCheckConstraint("ck_movimiento_monto", "monto > 0"));
        });

        mb.Entity<Prestamo>(e =>
        {
            e.HasOne(x => x.Caja).WithMany().HasForeignKey(x => x.CajaId);
            e.HasOne(x => x.Ahorrador).WithMany().HasForeignKey(x => x.AhorradorId);
            e.HasOne(x => x.Aval).WithMany().HasForeignKey(x => x.AvalAhorradorId);
            e.HasOne(x => x.Autorizador).WithMany().HasForeignKey(x => x.AutorizadoPor);
            e.HasIndex(x => x.Folio).IsUnique();
            e.HasIndex(x => new { x.CajaId, x.Estado });
            e.Property(x => x.Folio).HasMaxLength(20);
            e.Property(x => x.TasaMensual).HasPrecision(5, 2);
            e.Property(x => x.Garantia).HasEnumColumn();
            e.Property(x => x.Estado).HasEnumColumn();
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_prestamo_monto", "monto > 0 AND plazo_quincenas > 0");
                t.HasCheckConstraint("ck_prestamo_tasa", "tasa_mensual BETWEEN 1.00 AND 2.00");
                t.HasCheckConstraint("ck_prestamo_aval", "garantia <> 'PAGARE_AVAL' OR aval_ahorrador_id IS NOT NULL");
                t.HasCheckConstraint("ck_prestamo_excedente", "excedente_aval >= 0 AND excedente_aval <= monto");
            });
        });

        mb.Entity<PrestamoAmortizacion>(e =>
        {
            e.HasOne(x => x.Prestamo).WithMany(x => x.Amortizacion).HasForeignKey(x => x.PrestamoId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Periodo).WithMany().HasForeignKey(x => x.PeriodoId);
            e.HasIndex(x => new { x.PrestamoId, x.Numero }).IsUnique();
            e.Property(x => x.Estado).HasEnumColumn();
            e.HasIndex(x => new { x.PeriodoId, x.Estado });
            e.ToTable(t => t.HasCheckConstraint("ck_amortizacion_montos", "capital >= 0 AND interes >= 0 AND saldo_despues >= 0"));
        });

        mb.Entity<Incidencia>(e =>
        {
            e.HasOne(x => x.Ahorrador).WithMany().HasForeignKey(x => x.AhorradorId);
            e.HasOne(x => x.Periodo).WithMany().HasForeignKey(x => x.PeriodoId);
            e.HasIndex(x => new { x.AhorradorId, x.PeriodoId, x.Tipo }).IsUnique();
            e.Property(x => x.Tipo).HasEnumColumn();
            e.Property(x => x.Nota).HasMaxLength(500);
        });

        mb.Entity<Importacion>(e =>
        {
            e.HasOne(x => x.Caja).WithMany().HasForeignKey(x => x.CajaId);
            e.HasOne(x => x.Usuario).WithMany().HasForeignKey(x => x.UsuarioId);
            e.Property(x => x.Archivo).HasMaxLength(255);
            e.Property(x => x.Formato).HasEnumColumn();
            e.Property(x => x.Estado).HasEnumColumn();
        });

        mb.Entity<ImportacionDetalle>(e =>
        {
            e.HasOne(x => x.Importacion).WithMany(x => x.Detalles).HasForeignKey(x => x.ImportacionId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.NumEmpleado).HasMaxLength(20);
            e.Property(x => x.Datos).HasColumnType("longtext");
            e.Property(x => x.MensajeError).HasMaxLength(500);
            e.Property(x => x.Tipo).HasEnumColumn();
            e.Property(x => x.Validacion).HasEnumColumn();
        });

        mb.Entity<Ciclo>(e =>
        {
            e.HasOne(x => x.Caja).WithMany().HasForeignKey(x => x.CajaId);
            e.HasIndex(x => new { x.CajaId, x.Anio }).IsUnique();
            e.Property(x => x.Estado).HasEnumColumn();
        });

        mb.Entity<CicloDistribucion>(e =>
        {
            e.HasKey(x => new { x.CicloId, x.AhorradorId });
            e.HasOne(x => x.Ciclo).WithMany().HasForeignKey(x => x.CicloId);
            e.HasOne(x => x.Ahorrador).WithMany().HasForeignKey(x => x.AhorradorId);
            e.Property(x => x.SaldoDias).HasPrecision(16, 2);
            e.Property(x => x.Participacion).HasPrecision(10, 6);
            e.ToTable(t => t.HasCheckConstraint("ck_distribucion_participacion", "participacion BETWEEN 0 AND 1"));
        });

        mb.Entity<LayoutGenerado>(e =>
        {
            e.HasOne(x => x.Caja).WithMany().HasForeignKey(x => x.CajaId);
            e.HasOne<Periodo>().WithMany().HasForeignKey(x => x.PeriodoId);
            e.HasOne<Ciclo>().WithMany().HasForeignKey(x => x.CicloId);
            e.HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId);
            e.Property(x => x.Tipo).HasEnumColumn();
            e.Property(x => x.Formato).HasMaxLength(30);
            e.Property(x => x.NombreArchivo).HasMaxLength(255);
            e.Property(x => x.SumaControl).HasMaxLength(64);
            e.Property(x => x.ImporteTotal).HasPrecision(16, 2);
        });

        mb.Entity<InteresDevengado>(e =>
        {
            e.HasOne(x => x.Prestamo).WithMany().HasForeignKey(x => x.PrestamoId);
            e.HasOne(x => x.Periodo).WithMany().HasForeignKey(x => x.PeriodoId);
            e.HasIndex(x => new { x.PrestamoId, x.FechaCorte }).IsUnique();
        });

        mb.Entity<TareaProgramada>(e =>
        {
            e.HasIndex(x => x.Clave).IsUnique();
            e.Property(x => x.Clave).HasMaxLength(40);
            e.Property(x => x.Frecuencia).HasEnumColumn();
        });

        mb.Entity<TareaEjecucion>(e =>
        {
            e.HasOne(x => x.Tarea).WithMany().HasForeignKey(x => x.TareaId);
            e.Property(x => x.Estado).HasEnumColumn();
            e.Property(x => x.Resumen).HasMaxLength(500);
        });

        AplicarNombresSnakeCase(mb);
    }

    // Tablas, columnas, índices y llaves foráneas en snake_case (el esquema debe leerse bien en SQL).
    private static void AplicarNombresSnakeCase(ModelBuilder mb)
    {
        foreach (var entidad in mb.Model.GetEntityTypes())
        {
            var tabla = ASnake(entidad.ClrType.Name);
            entidad.SetTableName(tabla);
            foreach (var p in entidad.GetProperties()) p.SetColumnName(ASnake(p.Name));
            foreach (var i in entidad.GetIndexes())
                i.SetDatabaseName((i.IsUnique ? "ux_" : "ix_") + tabla + "_" + string.Join("_", i.Properties.Select(p => ASnake(p.Name))));
            foreach (var fk in entidad.GetForeignKeys())
            {
                var columnas = string.Join("_", fk.Properties.Select(p => ASnake(p.Name)));
                // Solo los hijos que no tienen sentido sin su padre se borran en cascada; el resto, restringido.
                fk.DeleteBehavior = CascadaPermitida.Contains($"{tabla}.{columnas}") ? DeleteBehavior.Cascade : DeleteBehavior.Restrict;
                fk.SetConstraintName($"fk_{tabla}_{columnas}");
            }
        }
    }

    private static readonly HashSet<string> CascadaPermitida =
    [
        "usuario_caja.usuario_id", "refresh_token.usuario_id",
        "prestamo_amortizacion.prestamo_id", "importacion_detalle.importacion_id"
    ];

    private static string ASnake(string nombre) => SeparadorMayusculas.Replace(nombre, "_").ToLowerInvariant();

    private static readonly Regex SeparadorMayusculas = new("(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);
}

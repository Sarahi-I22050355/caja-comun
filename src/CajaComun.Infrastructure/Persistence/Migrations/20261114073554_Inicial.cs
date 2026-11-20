using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CajaComun.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Inicial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "empresa",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    nombre = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    activa = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_empresa", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "tarea_programada",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    clave = table.Column<string>(type: "varchar(40)", maxLength: 40, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    nombre = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    frecuencia = table.Column<string>(type: "enum('DIARIA','QUINCENAL','MENSUAL')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    hora = table.Column<TimeOnly>(type: "time(6)", nullable: false),
                    activa = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ultima_ejecucion = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    proxima_ejecucion = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tarea_programada", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "usuario",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    nombre = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    correo = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    hash_password = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rol = table.Column<string>(type: "enum('ADMINISTRADOR','CAPTURISTA','CONSULTA')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    acceso_todas_cajas = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    activo = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ultimo_acceso = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuario", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "caja",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    empresa_id = table.Column<int>(type: "int", nullable: false),
                    nombre = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    prefijo = table.Column<string>(type: "varchar(5)", maxLength: 5, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    tasa_interes_mensual = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    frecuencia_nomina = table.Column<string>(type: "enum('QUINCENAL','SEMANAL')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    inicio_ciclo = table.Column<DateOnly>(type: "date", nullable: false),
                    fin_ciclo = table.Column<DateOnly>(type: "date", nullable: false),
                    activa = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_caja", x => x.id);
                    table.CheckConstraint("ck_caja_tasa", "tasa_interes_mensual BETWEEN 1.00 AND 2.00");
                    table.ForeignKey(
                        name: "fk_caja_empresa_id",
                        column: x => x.empresa_id,
                        principalTable: "empresa",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "tarea_ejecucion",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    tarea_id = table.Column<int>(type: "int", nullable: false),
                    inicio = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    fin = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    estado = table.Column<string>(type: "enum('EN_CURSO','EXITO','ERROR')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    resumen = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tarea_ejecucion", x => x.id);
                    table.ForeignKey(
                        name: "fk_tarea_ejecucion_tarea_id",
                        column: x => x.tarea_id,
                        principalTable: "tarea_programada",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "refresh_token",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    usuario_id = table.Column<int>(type: "int", nullable: false),
                    token_hash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    creado = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    expira = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    revocado = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_token", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_token_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "ahorrador",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    caja_id = table.Column<int>(type: "int", nullable: false),
                    num_empleado = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    nombre = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    apellidos = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    rfc = table.Column<string>(type: "varchar(13)", maxLength: 13, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    fecha_nacimiento = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_ingreso = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_baja = table.Column<DateOnly>(type: "date", nullable: true),
                    estado = table.Column<string>(type: "enum('ACTIVO','LICENCIA','BAJA')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sueldo_quincenal = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    clabe = table.Column<string>(type: "char(18)", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    departamento = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ahorrador", x => x.id);
                    table.CheckConstraint("ck_ahorrador_clabe", "clabe REGEXP '^[0-9]{18}$'");
                    table.CheckConstraint("ck_ahorrador_sueldo", "sueldo_quincenal >= 0");
                    table.ForeignKey(
                        name: "fk_ahorrador_caja_id",
                        column: x => x.caja_id,
                        principalTable: "caja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "ciclo",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    caja_id = table.Column<int>(type: "int", nullable: false),
                    anio = table.Column<int>(type: "int", nullable: false),
                    estado = table.Column<string>(type: "enum('ABIERTO','EN_CIERRE','CERRADO')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    interes_total = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    fecha_cierre = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ciclo", x => x.id);
                    table.ForeignKey(
                        name: "fk_ciclo_caja_id",
                        column: x => x.caja_id,
                        principalTable: "caja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "importacion",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    caja_id = table.Column<int>(type: "int", nullable: false),
                    archivo = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    formato = table.Column<string>(type: "enum('XLSX','XML','TXT','CSV')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    usuario_id = table.Column<int>(type: "int", nullable: false),
                    fecha = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    estado = table.Column<string>(type: "enum('PREVIA','APLICADA','CANCELADA')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_importacion", x => x.id);
                    table.ForeignKey(
                        name: "fk_importacion_caja_id",
                        column: x => x.caja_id,
                        principalTable: "caja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_importacion_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "periodo",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    caja_id = table.Column<int>(type: "int", nullable: false),
                    anio = table.Column<int>(type: "int", nullable: false),
                    numero = table.Column<int>(type: "int", nullable: false),
                    fecha_inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_fin = table.Column<DateOnly>(type: "date", nullable: false),
                    estado = table.Column<string>(type: "enum('ABIERTO','APLICADO','CERRADO')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_periodo", x => x.id);
                    table.CheckConstraint("ck_periodo_fechas", "fecha_fin >= fecha_inicio AND numero BETWEEN 1 AND 53");
                    table.ForeignKey(
                        name: "fk_periodo_caja_id",
                        column: x => x.caja_id,
                        principalTable: "caja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "usuario_caja",
                columns: table => new
                {
                    usuario_id = table.Column<int>(type: "int", nullable: false),
                    caja_id = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usuario_caja", x => new { x.usuario_id, x.caja_id });
                    table.ForeignKey(
                        name: "fk_usuario_caja_caja_id",
                        column: x => x.caja_id,
                        principalTable: "caja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_usuario_caja_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "cuota_ahorro",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ahorrador_id = table.Column<int>(type: "int", nullable: false),
                    cuota_ordinaria = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    cuota_voluntaria = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    voluntario_activo = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    vigente_desde = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cuota_ahorro", x => x.id);
                    table.CheckConstraint("ck_cuota_montos", "cuota_ordinaria >= 0 AND cuota_voluntaria >= 0");
                    table.ForeignKey(
                        name: "fk_cuota_ahorro_ahorrador_id",
                        column: x => x.ahorrador_id,
                        principalTable: "ahorrador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "prestamo",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    folio = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    caja_id = table.Column<int>(type: "int", nullable: false),
                    ahorrador_id = table.Column<int>(type: "int", nullable: false),
                    monto = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    plazo_quincenas = table.Column<int>(type: "int", nullable: false),
                    tasa_mensual = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false),
                    garantia = table.Column<string>(type: "enum('AHORRO_80','PAGARE_AVAL')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    aval_ahorrador_id = table.Column<int>(type: "int", nullable: true),
                    estado = table.Column<string>(type: "enum('PENDIENTE','VIGENTE','LIQUIDADO','RECHAZADO')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    fecha_solicitud = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_autorizacion = table.Column<DateOnly>(type: "date", nullable: true),
                    autorizado_por = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prestamo", x => x.id);
                    table.CheckConstraint("ck_prestamo_aval", "garantia <> 'PAGARE_AVAL' OR aval_ahorrador_id IS NOT NULL");
                    table.CheckConstraint("ck_prestamo_monto", "monto > 0 AND plazo_quincenas > 0");
                    table.CheckConstraint("ck_prestamo_tasa", "tasa_mensual BETWEEN 1.00 AND 2.00");
                    table.ForeignKey(
                        name: "fk_prestamo_ahorrador_id",
                        column: x => x.ahorrador_id,
                        principalTable: "ahorrador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_prestamo_autorizado_por",
                        column: x => x.autorizado_por,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_prestamo_aval_ahorrador_id",
                        column: x => x.aval_ahorrador_id,
                        principalTable: "ahorrador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_prestamo_caja_id",
                        column: x => x.caja_id,
                        principalTable: "caja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "ciclo_distribucion",
                columns: table => new
                {
                    ciclo_id = table.Column<int>(type: "int", nullable: false),
                    ahorrador_id = table.Column<int>(type: "int", nullable: false),
                    saldo_dias = table.Column<decimal>(type: "decimal(16,2)", precision: 16, scale: 2, nullable: false),
                    saldo_promedio = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    dias_activos = table.Column<int>(type: "int", nullable: false),
                    participacion = table.Column<decimal>(type: "decimal(10,6)", precision: 10, scale: 6, nullable: false),
                    interes_asignado = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    ahorro_liquidado = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ciclo_distribucion", x => new { x.ciclo_id, x.ahorrador_id });
                    table.CheckConstraint("ck_distribucion_participacion", "participacion BETWEEN 0 AND 1");
                    table.ForeignKey(
                        name: "fk_ciclo_distribucion_ahorrador_id",
                        column: x => x.ahorrador_id,
                        principalTable: "ahorrador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ciclo_distribucion_ciclo_id",
                        column: x => x.ciclo_id,
                        principalTable: "ciclo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "importacion_detalle",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    importacion_id = table.Column<int>(type: "int", nullable: false),
                    numero_fila = table.Column<int>(type: "int", nullable: false),
                    tipo = table.Column<string>(type: "enum('ALTA','BAJA','SIN_CAMBIO')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    num_empleado = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    datos = table.Column<string>(type: "longtext", maxLength: 200, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    validacion = table.Column<string>(type: "enum('VALIDO','ERROR')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    mensaje_error = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_importacion_detalle", x => x.id);
                    table.ForeignKey(
                        name: "fk_importacion_detalle_importacion_id",
                        column: x => x.importacion_id,
                        principalTable: "importacion",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "incidencia",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    ahorrador_id = table.Column<int>(type: "int", nullable: false),
                    periodo_id = table.Column<int>(type: "int", nullable: false),
                    tipo = table.Column<string>(type: "enum('LICENCIA','INCAPACIDAD','BAJA','CAMBIO_CUOTA','SUSPENDE_VOLUNTARIO')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    valor = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: true),
                    nota = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidencia", x => x.id);
                    table.ForeignKey(
                        name: "fk_incidencia_ahorrador_id",
                        column: x => x.ahorrador_id,
                        principalTable: "ahorrador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_incidencia_periodo_id",
                        column: x => x.periodo_id,
                        principalTable: "periodo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "layout_generado",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    tipo = table.Column<string>(type: "enum('NOMINA','SPEI')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    caja_id = table.Column<int>(type: "int", nullable: false),
                    periodo_id = table.Column<int>(type: "int", nullable: true),
                    ciclo_id = table.Column<int>(type: "int", nullable: true),
                    formato = table.Column<string>(type: "varchar(10)", maxLength: 10, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    nombre_archivo = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    registros = table.Column<int>(type: "int", nullable: false),
                    importe_total = table.Column<decimal>(type: "decimal(16,2)", precision: 16, scale: 2, nullable: false),
                    suma_control = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    usuario_id = table.Column<int>(type: "int", nullable: false),
                    fecha = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_layout_generado", x => x.id);
                    table.ForeignKey(
                        name: "fk_layout_generado_caja_id",
                        column: x => x.caja_id,
                        principalTable: "caja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_layout_generado_ciclo_id",
                        column: x => x.ciclo_id,
                        principalTable: "ciclo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_layout_generado_periodo_id",
                        column: x => x.periodo_id,
                        principalTable: "periodo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_layout_generado_usuario_id",
                        column: x => x.usuario_id,
                        principalTable: "usuario",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "interes_devengado",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    prestamo_id = table.Column<int>(type: "int", nullable: false),
                    periodo_id = table.Column<int>(type: "int", nullable: false),
                    fecha_corte = table.Column<DateOnly>(type: "date", nullable: false),
                    saldo_insoluto = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    interes = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interes_devengado", x => x.id);
                    table.ForeignKey(
                        name: "fk_interes_devengado_periodo_id",
                        column: x => x.periodo_id,
                        principalTable: "periodo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_interes_devengado_prestamo_id",
                        column: x => x.prestamo_id,
                        principalTable: "prestamo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "movimiento",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    caja_id = table.Column<int>(type: "int", nullable: false),
                    ahorrador_id = table.Column<int>(type: "int", nullable: false),
                    periodo_id = table.Column<int>(type: "int", nullable: true),
                    prestamo_id = table.Column<int>(type: "int", nullable: true),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    tipo = table.Column<string>(type: "enum('AHORRO_ORDINARIO','AHORRO_VOLUNTARIO','DESEMBOLSO_PRESTAMO','ABONO_CAPITAL','ABONO_INTERES','INTERES_ANUAL','LIQUIDACION_CICLO')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    monto = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    referencia = table.Column<string>(type: "varchar(60)", maxLength: 60, nullable: true, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_movimiento", x => x.id);
                    table.CheckConstraint("ck_movimiento_monto", "monto > 0");
                    table.ForeignKey(
                        name: "fk_movimiento_ahorrador_id",
                        column: x => x.ahorrador_id,
                        principalTable: "ahorrador",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movimiento_caja_id",
                        column: x => x.caja_id,
                        principalTable: "caja",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movimiento_periodo_id",
                        column: x => x.periodo_id,
                        principalTable: "periodo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_movimiento_prestamo_id",
                        column: x => x.prestamo_id,
                        principalTable: "prestamo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateTable(
                name: "prestamo_amortizacion",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    prestamo_id = table.Column<int>(type: "int", nullable: false),
                    numero = table.Column<int>(type: "int", nullable: false),
                    periodo_id = table.Column<int>(type: "int", nullable: false),
                    fecha = table.Column<DateOnly>(type: "date", nullable: false),
                    capital = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    interes = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    total = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    saldo_despues = table.Column<decimal>(type: "decimal(14,2)", precision: 14, scale: 2, nullable: false),
                    estado = table.Column<string>(type: "enum('PROYECTADO','PROGRAMADO','APLICADO')", nullable: false, collation: "utf8mb4_unicode_ci")
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_prestamo_amortizacion", x => x.id);
                    table.CheckConstraint("ck_amortizacion_montos", "capital >= 0 AND interes >= 0 AND saldo_despues >= 0");
                    table.ForeignKey(
                        name: "fk_prestamo_amortizacion_periodo_id",
                        column: x => x.periodo_id,
                        principalTable: "periodo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_prestamo_amortizacion_prestamo_id",
                        column: x => x.prestamo_id,
                        principalTable: "prestamo",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4")
                .Annotation("Relational:Collation", "utf8mb4_unicode_ci");

            migrationBuilder.CreateIndex(
                name: "ux_ahorrador_caja_id_num_empleado",
                table: "ahorrador",
                columns: new[] { "caja_id", "num_empleado" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_caja_empresa_id_nombre",
                table: "caja",
                columns: new[] { "empresa_id", "nombre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_ciclo_caja_id_anio",
                table: "ciclo",
                columns: new[] { "caja_id", "anio" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ciclo_distribucion_ahorrador_id",
                table: "ciclo_distribucion",
                column: "ahorrador_id");

            migrationBuilder.CreateIndex(
                name: "ux_cuota_ahorro_ahorrador_id_vigente_desde",
                table: "cuota_ahorro",
                columns: new[] { "ahorrador_id", "vigente_desde" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_empresa_nombre",
                table: "empresa",
                column: "nombre",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_importacion_caja_id",
                table: "importacion",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "ix_importacion_usuario_id",
                table: "importacion",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_importacion_detalle_importacion_id",
                table: "importacion_detalle",
                column: "importacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_incidencia_periodo_id",
                table: "incidencia",
                column: "periodo_id");

            migrationBuilder.CreateIndex(
                name: "ux_incidencia_ahorrador_id_periodo_id_tipo",
                table: "incidencia",
                columns: new[] { "ahorrador_id", "periodo_id", "tipo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_interes_devengado_periodo_id",
                table: "interes_devengado",
                column: "periodo_id");

            migrationBuilder.CreateIndex(
                name: "ux_interes_devengado_prestamo_id_fecha_corte",
                table: "interes_devengado",
                columns: new[] { "prestamo_id", "fecha_corte" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_layout_generado_caja_id",
                table: "layout_generado",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "ix_layout_generado_ciclo_id",
                table: "layout_generado",
                column: "ciclo_id");

            migrationBuilder.CreateIndex(
                name: "ix_layout_generado_periodo_id",
                table: "layout_generado",
                column: "periodo_id");

            migrationBuilder.CreateIndex(
                name: "ix_layout_generado_usuario_id",
                table: "layout_generado",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_ahorrador_id",
                table: "movimiento",
                column: "ahorrador_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_caja_id",
                table: "movimiento",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_periodo_id",
                table: "movimiento",
                column: "periodo_id");

            migrationBuilder.CreateIndex(
                name: "ix_movimiento_prestamo_id",
                table: "movimiento",
                column: "prestamo_id");

            migrationBuilder.CreateIndex(
                name: "ux_periodo_caja_id_anio_numero",
                table: "periodo",
                columns: new[] { "caja_id", "anio", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_prestamo_ahorrador_id",
                table: "prestamo",
                column: "ahorrador_id");

            migrationBuilder.CreateIndex(
                name: "ix_prestamo_autorizado_por",
                table: "prestamo",
                column: "autorizado_por");

            migrationBuilder.CreateIndex(
                name: "ix_prestamo_aval_ahorrador_id",
                table: "prestamo",
                column: "aval_ahorrador_id");

            migrationBuilder.CreateIndex(
                name: "ix_prestamo_caja_id",
                table: "prestamo",
                column: "caja_id");

            migrationBuilder.CreateIndex(
                name: "ux_prestamo_folio",
                table: "prestamo",
                column: "folio",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_prestamo_amortizacion_periodo_id",
                table: "prestamo_amortizacion",
                column: "periodo_id");

            migrationBuilder.CreateIndex(
                name: "ux_prestamo_amortizacion_prestamo_id_numero",
                table: "prestamo_amortizacion",
                columns: new[] { "prestamo_id", "numero" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_token_usuario_id",
                table: "refresh_token",
                column: "usuario_id");

            migrationBuilder.CreateIndex(
                name: "ux_refresh_token_token_hash",
                table: "refresh_token",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tarea_ejecucion_tarea_id",
                table: "tarea_ejecucion",
                column: "tarea_id");

            migrationBuilder.CreateIndex(
                name: "ux_tarea_programada_clave",
                table: "tarea_programada",
                column: "clave",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_usuario_correo",
                table: "usuario",
                column: "correo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_usuario_caja_caja_id",
                table: "usuario_caja",
                column: "caja_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ciclo_distribucion");

            migrationBuilder.DropTable(
                name: "cuota_ahorro");

            migrationBuilder.DropTable(
                name: "importacion_detalle");

            migrationBuilder.DropTable(
                name: "incidencia");

            migrationBuilder.DropTable(
                name: "interes_devengado");

            migrationBuilder.DropTable(
                name: "layout_generado");

            migrationBuilder.DropTable(
                name: "movimiento");

            migrationBuilder.DropTable(
                name: "prestamo_amortizacion");

            migrationBuilder.DropTable(
                name: "refresh_token");

            migrationBuilder.DropTable(
                name: "tarea_ejecucion");

            migrationBuilder.DropTable(
                name: "usuario_caja");

            migrationBuilder.DropTable(
                name: "importacion");

            migrationBuilder.DropTable(
                name: "ciclo");

            migrationBuilder.DropTable(
                name: "periodo");

            migrationBuilder.DropTable(
                name: "prestamo");

            migrationBuilder.DropTable(
                name: "tarea_programada");

            migrationBuilder.DropTable(
                name: "ahorrador");

            migrationBuilder.DropTable(
                name: "usuario");

            migrationBuilder.DropTable(
                name: "caja");

            migrationBuilder.DropTable(
                name: "empresa");
        }
    }
}

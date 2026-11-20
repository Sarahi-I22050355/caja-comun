-- Caja Transportes del Bravo en ClickHouse (la base `bravo` la crea la imagen con CLICKHOUSE_DB).
-- Las tablas las llena `cargar-federacion` desde MariaDB; no se copian RFC, CLABE ni sueldos.
-- Fechas: el conector de Trino solo acepta Date entre 1970 y 2149, y hay ahorradores nacidos antes de 1970, así que
-- fecha_nacimiento se guarda como texto AAAA-MM-DD y las consultas la convierten con CAST(... AS date).
CREATE TABLE IF NOT EXISTS bravo.ahorrador (
    id               Int32,
    caja_id          Int32,
    num_empleado     String,
    nombre           String,
    apellidos        String,
    fecha_nacimiento String,
    fecha_ingreso    Date,
    fecha_baja       Nullable(Date),
    estado           String,
    departamento     String
) ENGINE = MergeTree ORDER BY id;

CREATE TABLE IF NOT EXISTS bravo.movimiento (
    id           Int64,
    caja_id      Int32,
    ahorrador_id Int32,
    prestamo_id  Nullable(Int32),
    fecha        Date,
    tipo         String,
    monto        Decimal(14, 2)
) ENGINE = MergeTree ORDER BY (ahorrador_id, fecha, id);

CREATE TABLE IF NOT EXISTS bravo.prestamo (
    id                 Int32,
    folio              String,
    caja_id            Int32,
    ahorrador_id       Int32,
    monto              Decimal(14, 2),
    plazo_quincenas    Int32,
    tasa_mensual       Decimal(5, 2),
    estado             String,
    fecha_solicitud    Date,
    fecha_autorizacion Nullable(Date)
) ENGINE = MergeTree ORDER BY id;

CREATE TABLE IF NOT EXISTS bravo.ciclo (
    id            Int32,
    caja_id       Int32,
    anio          Int32,
    estado        String,
    interes_total Decimal(14, 2)
) ENGINE = MergeTree ORDER BY id;

CREATE TABLE IF NOT EXISTS bravo.ciclo_distribucion (
    ciclo_id         Int32,
    ahorrador_id     Int32,
    interes_asignado Decimal(14, 2),
    ahorro_liquidado Decimal(14, 2),
    ahorro_retenido  Decimal(14, 2)
) ENGINE = MergeTree ORDER BY (ciclo_id, ahorrador_id);

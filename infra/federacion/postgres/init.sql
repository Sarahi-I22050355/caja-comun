-- Caja Aceros del Norte en PostgreSQL. Solo lo necesario para las consultas de la Parte 2: no se copian RFC, CLABE ni sueldos.
-- Las tablas las llena `cargar-federacion` desde MariaDB; no se escribe aquí a mano.
CREATE TABLE IF NOT EXISTS ahorrador (
    id               integer      PRIMARY KEY,
    caja_id          integer      NOT NULL,
    num_empleado     varchar(20)  NOT NULL,
    nombre           varchar(200) NOT NULL,
    apellidos        varchar(200) NOT NULL,
    fecha_nacimiento date         NOT NULL,
    fecha_ingreso    date         NOT NULL,
    fecha_baja       date,
    estado           varchar(20)  NOT NULL,
    departamento     varchar(200) NOT NULL
);

CREATE TABLE IF NOT EXISTS movimiento (
    id           bigint        PRIMARY KEY,
    caja_id      integer       NOT NULL,
    ahorrador_id integer       NOT NULL,
    prestamo_id  integer,
    fecha        date          NOT NULL,
    tipo         varchar(30)   NOT NULL,
    monto        numeric(14,2) NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_movimiento_ahorrador_fecha ON movimiento (ahorrador_id, fecha);

CREATE TABLE IF NOT EXISTS prestamo (
    id                 integer       PRIMARY KEY,
    folio              varchar(20)   NOT NULL,
    caja_id            integer       NOT NULL,
    ahorrador_id       integer       NOT NULL,
    monto              numeric(14,2) NOT NULL,
    plazo_quincenas    integer       NOT NULL,
    tasa_mensual       numeric(5,2)  NOT NULL,
    estado             varchar(20)   NOT NULL,
    fecha_solicitud    date          NOT NULL,
    fecha_autorizacion date
);

CREATE TABLE IF NOT EXISTS ciclo (
    id            integer       PRIMARY KEY,
    caja_id       integer       NOT NULL,
    anio          integer       NOT NULL,
    estado        varchar(20)   NOT NULL,
    interes_total numeric(14,2) NOT NULL
);

CREATE TABLE IF NOT EXISTS ciclo_distribucion (
    ciclo_id         integer       NOT NULL,
    ahorrador_id     integer       NOT NULL,
    interes_asignado numeric(14,2) NOT NULL,
    ahorro_liquidado numeric(14,2) NOT NULL,
    ahorro_retenido  numeric(14,2) NOT NULL,
    PRIMARY KEY (ciclo_id, ahorrador_id)
);

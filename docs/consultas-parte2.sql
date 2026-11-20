-- Parte 2: consultas distribuidas en SQL de Trino.
--
-- Cada caja vive en un motor distinto y Trino los une en una sola consulta:
--   mariadb.caja_comun   MariaDB     Caja GIMSA (prefijo GIM) y las cajas que no se hayan pasado a otro motor.
--                                    También guarda el catálogo de cajas y empresas.
--   postgresql.public    PostgreSQL  Caja Aceros del Norte (prefijo ACE).
--   clickhouse.bravo     ClickHouse  Caja Transportes del Bravo (prefijo TDB).
-- En ClickHouse la fecha de nacimiento se guarda como texto (el conector de Trino no acepta fechas anteriores a 1970),
-- por eso la rama de ClickHouse la convierte con CAST(fecha_nacimiento AS date).
-- Los motores se cargan con `scripts/federacion.ps1 cargar` (ver docs/consultas-parte2.md).
--
-- Parámetros que la API sustituye antes de enviar la consulta a Trino:
--   {{anio}}         ejercicio consultado (Q4 y Q5), por ejemplo 2024
--   {{anio_sig}}     el año siguiente, para escribir el año como intervalo [1-ene, 1-ene siguiente)
--   {{fecha_corte}}  fecha con la que se calcula la edad (Q3), por ejemplo DATE '2026-11-19'
-- Para correrlas a mano con el cliente de Trino, reemplaza esos tres valores.
--
-- Cada bloque `-- @consulta N` es una consulta. Las columnas y el orden son los mismos que los de la consulta
-- equivalente directa sobre MariaDB (docs/consultas-parte2-mariadb.sql), que es con la que se compara el resultado.

-- @consulta 1
-- Q1. Todos los ahorradores con su estatus y la caja a la que pertenecen.
WITH ahorradores AS (
    SELECT 'MariaDB' AS motor, id, caja_id, num_empleado, nombre, apellidos, fecha_nacimiento, fecha_ingreso, fecha_baja, estado
      FROM mariadb.caja_comun.ahorrador
     WHERE caja_id NOT IN (SELECT id FROM mariadb.caja_comun.caja WHERE prefijo IN ('ACE', 'TDB'))
    UNION ALL
    SELECT 'PostgreSQL', id, caja_id, num_empleado, nombre, apellidos, fecha_nacimiento, fecha_ingreso, fecha_baja, estado
      FROM postgresql.public.ahorrador
    UNION ALL
    SELECT 'ClickHouse', id, caja_id, num_empleado, nombre, apellidos, CAST(fecha_nacimiento AS date), fecha_ingreso, fecha_baja, estado
      FROM clickhouse.bravo.ahorrador
)
SELECT c.nombre AS caja,
       e.nombre AS empresa,
       a.motor,
       a.num_empleado,
       concat(a.nombre, ' ', a.apellidos) AS ahorrador,
       a.estado,
       a.fecha_ingreso
  FROM ahorradores a
  JOIN mariadb.caja_comun.caja c ON c.id = a.caja_id
  JOIN mariadb.caja_comun.empresa e ON e.id = c.empresa_id
 ORDER BY c.nombre, a.num_empleado

-- @consulta 2
-- Q2. Ahorradores dados de baja.
WITH ahorradores AS (
    SELECT 'MariaDB' AS motor, id, caja_id, num_empleado, nombre, apellidos, fecha_nacimiento, fecha_ingreso, fecha_baja, estado
      FROM mariadb.caja_comun.ahorrador
     WHERE caja_id NOT IN (SELECT id FROM mariadb.caja_comun.caja WHERE prefijo IN ('ACE', 'TDB'))
    UNION ALL
    SELECT 'PostgreSQL', id, caja_id, num_empleado, nombre, apellidos, fecha_nacimiento, fecha_ingreso, fecha_baja, estado
      FROM postgresql.public.ahorrador
    UNION ALL
    SELECT 'ClickHouse', id, caja_id, num_empleado, nombre, apellidos, CAST(fecha_nacimiento AS date), fecha_ingreso, fecha_baja, estado
      FROM clickhouse.bravo.ahorrador
)
SELECT c.nombre AS caja,
       a.motor,
       a.num_empleado,
       concat(a.nombre, ' ', a.apellidos) AS ahorrador,
       a.fecha_ingreso,
       a.fecha_baja,
       date_diff('day', a.fecha_ingreso, a.fecha_baja) AS dias_en_la_caja
  FROM ahorradores a
  JOIN mariadb.caja_comun.caja c ON c.id = a.caja_id
 WHERE a.estado = 'BAJA'
 ORDER BY c.nombre, a.num_empleado

-- @consulta 3
-- Q3. Ahorradores con su edad en años cumplidos a la fecha de corte.
WITH ahorradores AS (
    SELECT 'MariaDB' AS motor, id, caja_id, num_empleado, nombre, apellidos, fecha_nacimiento, fecha_ingreso, fecha_baja, estado
      FROM mariadb.caja_comun.ahorrador
     WHERE caja_id NOT IN (SELECT id FROM mariadb.caja_comun.caja WHERE prefijo IN ('ACE', 'TDB'))
    UNION ALL
    SELECT 'PostgreSQL', id, caja_id, num_empleado, nombre, apellidos, fecha_nacimiento, fecha_ingreso, fecha_baja, estado
      FROM postgresql.public.ahorrador
    UNION ALL
    SELECT 'ClickHouse', id, caja_id, num_empleado, nombre, apellidos, CAST(fecha_nacimiento AS date), fecha_ingreso, fecha_baja, estado
      FROM clickhouse.bravo.ahorrador
)
SELECT c.nombre AS caja,
       a.motor,
       a.num_empleado,
       concat(a.nombre, ' ', a.apellidos) AS ahorrador,
       a.estado,
       a.fecha_nacimiento,
       date_diff('year', a.fecha_nacimiento, {{fecha_corte}}) AS edad
  FROM ahorradores a
  JOIN mariadb.caja_comun.caja c ON c.id = a.caja_id
 ORDER BY c.nombre, a.num_empleado

-- @consulta 4
-- Q4. Ahorradores con préstamos otorgados (vigentes o liquidados) en el año {{anio}}.
WITH ahorradores AS (
    SELECT 'MariaDB' AS motor, id, caja_id, num_empleado, nombre, apellidos, estado
      FROM mariadb.caja_comun.ahorrador
     WHERE caja_id NOT IN (SELECT id FROM mariadb.caja_comun.caja WHERE prefijo IN ('ACE', 'TDB'))
    UNION ALL
    SELECT 'PostgreSQL', id, caja_id, num_empleado, nombre, apellidos, estado
      FROM postgresql.public.ahorrador
    UNION ALL
    SELECT 'ClickHouse', id, caja_id, num_empleado, nombre, apellidos, estado
      FROM clickhouse.bravo.ahorrador
),
prestamos AS (
    SELECT ahorrador_id, monto, fecha_autorizacion
      FROM mariadb.caja_comun.prestamo
     WHERE caja_id NOT IN (SELECT id FROM mariadb.caja_comun.caja WHERE prefijo IN ('ACE', 'TDB'))
       AND estado IN ('VIGENTE', 'LIQUIDADO')
       AND fecha_autorizacion >= DATE '{{anio}}-01-01' AND fecha_autorizacion < DATE '{{anio_sig}}-01-01'
    UNION ALL
    SELECT ahorrador_id, monto, fecha_autorizacion
      FROM postgresql.public.prestamo
     WHERE estado IN ('VIGENTE', 'LIQUIDADO')
       AND fecha_autorizacion >= DATE '{{anio}}-01-01' AND fecha_autorizacion < DATE '{{anio_sig}}-01-01'
    UNION ALL
    SELECT ahorrador_id, monto, fecha_autorizacion
      FROM clickhouse.bravo.prestamo
     WHERE estado IN ('VIGENTE', 'LIQUIDADO')
       AND fecha_autorizacion >= DATE '{{anio}}-01-01' AND fecha_autorizacion < DATE '{{anio_sig}}-01-01'
)
SELECT c.nombre AS caja,
       a.motor,
       a.num_empleado,
       concat(a.nombre, ' ', a.apellidos) AS ahorrador,
       a.estado,
       count(*) AS prestamos,
       sum(p.monto) AS monto_total,
       min(p.fecha_autorizacion) AS primer_prestamo
  FROM prestamos p
  JOIN ahorradores a ON a.id = p.ahorrador_id
  JOIN mariadb.caja_comun.caja c ON c.id = a.caja_id
 GROUP BY c.nombre, a.motor, a.num_empleado, a.nombre, a.apellidos, a.estado
 ORDER BY c.nombre, a.num_empleado

-- @consulta 5
-- Q5. Ahorradores que estuvieron en la caja durante el año {{anio}}, con su utilidad (el rendimiento que les asignó el
-- cierre de ciclo) y el balance de sus movimientos del año.
--   entradas = ahorro ordinario + ahorro voluntario + abonos de capital + abonos de interés
--   salidas  = desembolsos de préstamo + rendimiento pagado + liquidaciones de ciclo
--   balance  = entradas - salidas
WITH ahorradores AS (
    SELECT 'MariaDB' AS motor, id, caja_id, num_empleado, nombre, apellidos, fecha_ingreso, fecha_baja, estado
      FROM mariadb.caja_comun.ahorrador
     WHERE caja_id NOT IN (SELECT id FROM mariadb.caja_comun.caja WHERE prefijo IN ('ACE', 'TDB'))
    UNION ALL
    SELECT 'PostgreSQL', id, caja_id, num_empleado, nombre, apellidos, fecha_ingreso, fecha_baja, estado
      FROM postgresql.public.ahorrador
    UNION ALL
    SELECT 'ClickHouse', id, caja_id, num_empleado, nombre, apellidos, fecha_ingreso, fecha_baja, estado
      FROM clickhouse.bravo.ahorrador
),
movimientos AS (
    SELECT ahorrador_id, tipo, monto
      FROM mariadb.caja_comun.movimiento
     WHERE caja_id NOT IN (SELECT id FROM mariadb.caja_comun.caja WHERE prefijo IN ('ACE', 'TDB'))
       AND fecha >= DATE '{{anio}}-01-01' AND fecha < DATE '{{anio_sig}}-01-01'
    UNION ALL
    SELECT ahorrador_id, tipo, monto
      FROM postgresql.public.movimiento
     WHERE fecha >= DATE '{{anio}}-01-01' AND fecha < DATE '{{anio_sig}}-01-01'
    UNION ALL
    SELECT ahorrador_id, tipo, monto
      FROM clickhouse.bravo.movimiento
     WHERE fecha >= DATE '{{anio}}-01-01' AND fecha < DATE '{{anio_sig}}-01-01'
),
resumen AS (
    SELECT ahorrador_id,
           sum(CASE WHEN tipo IN ('AHORRO_ORDINARIO', 'AHORRO_VOLUNTARIO') THEN monto ELSE 0 END) AS ahorro_aportado,
           sum(CASE WHEN tipo IN ('ABONO_CAPITAL', 'ABONO_INTERES') THEN monto ELSE 0 END) AS abonos_prestamo,
           sum(CASE WHEN tipo = 'DESEMBOLSO_PRESTAMO' THEN monto ELSE 0 END) AS prestamos_recibidos,
           sum(CASE WHEN tipo = 'INTERES_ANUAL' THEN monto ELSE 0 END) AS rendimiento_pagado,
           sum(CASE WHEN tipo = 'LIQUIDACION_CICLO' THEN monto ELSE 0 END) AS liquidaciones,
           sum(CASE WHEN tipo IN ('AHORRO_ORDINARIO', 'AHORRO_VOLUNTARIO', 'ABONO_CAPITAL', 'ABONO_INTERES') THEN monto
                    ELSE -monto END) AS balance
      FROM movimientos
     GROUP BY ahorrador_id
),
utilidades AS (
    SELECT d.ahorrador_id, d.interes_asignado
      FROM mariadb.caja_comun.ciclo_distribucion d
      JOIN mariadb.caja_comun.ciclo ci ON ci.id = d.ciclo_id
     WHERE ci.anio = {{anio}}
       AND ci.caja_id NOT IN (SELECT id FROM mariadb.caja_comun.caja WHERE prefijo IN ('ACE', 'TDB'))
    UNION ALL
    SELECT d.ahorrador_id, d.interes_asignado
      FROM postgresql.public.ciclo_distribucion d
      JOIN postgresql.public.ciclo ci ON ci.id = d.ciclo_id
     WHERE ci.anio = {{anio}}
    UNION ALL
    SELECT d.ahorrador_id, d.interes_asignado
      FROM clickhouse.bravo.ciclo_distribucion d
      JOIN clickhouse.bravo.ciclo ci ON ci.id = d.ciclo_id
     WHERE ci.anio = {{anio}}
)
SELECT c.nombre AS caja,
       a.motor,
       a.num_empleado,
       concat(a.nombre, ' ', a.apellidos) AS ahorrador,
       a.estado,
       coalesce(sum(u.interes_asignado), 0) AS utilidad,
       coalesce(max(r.ahorro_aportado), 0) AS ahorro_aportado,
       coalesce(max(r.abonos_prestamo), 0) AS abonos_prestamo,
       coalesce(max(r.prestamos_recibidos), 0) AS prestamos_recibidos,
       coalesce(max(r.rendimiento_pagado), 0) AS rendimiento_pagado,
       coalesce(max(r.liquidaciones), 0) AS liquidaciones,
       coalesce(max(r.balance), 0) AS balance
  FROM ahorradores a
  JOIN mariadb.caja_comun.caja c ON c.id = a.caja_id
  LEFT JOIN resumen r ON r.ahorrador_id = a.id
  LEFT JOIN utilidades u ON u.ahorrador_id = a.id
 WHERE a.fecha_ingreso < DATE '{{anio_sig}}-01-01'
   AND (a.fecha_baja IS NULL OR a.fecha_baja >= DATE '{{anio}}-01-01')
 GROUP BY c.nombre, a.motor, a.num_empleado, a.nombre, a.apellidos, a.estado
 ORDER BY c.nombre, a.num_empleado

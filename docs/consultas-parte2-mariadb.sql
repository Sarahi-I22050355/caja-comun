-- Parte 2: las mismas cinco consultas, directas sobre MariaDB (que guarda todas las cajas).
-- Sirven para comprobar que el resultado federado (docs/consultas-parte2.sql) es el mismo: tienen las mismas columnas, y
-- la API y `verificar-federacion` comparan fila por fila. La columna `motor` sale de la caja (prefijo), que es como se
-- repartió cada caja entre los motores.
-- Parámetros: {{anio}}, {{anio_sig}} y {{fecha_corte}}, igual que en docs/consultas-parte2.sql.

-- @consulta 1
SELECT c.nombre AS caja,
       e.nombre AS empresa,
       CASE c.prefijo WHEN 'ACE' THEN 'PostgreSQL' WHEN 'TDB' THEN 'ClickHouse' ELSE 'MariaDB' END AS motor,
       a.num_empleado,
       CONCAT(a.nombre, ' ', a.apellidos) AS ahorrador,
       a.estado,
       a.fecha_ingreso
  FROM ahorrador a
  JOIN caja c ON c.id = a.caja_id
  JOIN empresa e ON e.id = c.empresa_id
 ORDER BY c.nombre, a.num_empleado

-- @consulta 2
SELECT c.nombre AS caja,
       CASE c.prefijo WHEN 'ACE' THEN 'PostgreSQL' WHEN 'TDB' THEN 'ClickHouse' ELSE 'MariaDB' END AS motor,
       a.num_empleado,
       CONCAT(a.nombre, ' ', a.apellidos) AS ahorrador,
       a.fecha_ingreso,
       a.fecha_baja,
       DATEDIFF(a.fecha_baja, a.fecha_ingreso) AS dias_en_la_caja
  FROM ahorrador a
  JOIN caja c ON c.id = a.caja_id
 WHERE a.estado = 'BAJA'
 ORDER BY c.nombre, a.num_empleado

-- @consulta 3
SELECT c.nombre AS caja,
       CASE c.prefijo WHEN 'ACE' THEN 'PostgreSQL' WHEN 'TDB' THEN 'ClickHouse' ELSE 'MariaDB' END AS motor,
       a.num_empleado,
       CONCAT(a.nombre, ' ', a.apellidos) AS ahorrador,
       a.estado,
       a.fecha_nacimiento,
       TIMESTAMPDIFF(YEAR, a.fecha_nacimiento, {{fecha_corte}}) AS edad
  FROM ahorrador a
  JOIN caja c ON c.id = a.caja_id
 ORDER BY c.nombre, a.num_empleado

-- @consulta 4
SELECT c.nombre AS caja,
       CASE c.prefijo WHEN 'ACE' THEN 'PostgreSQL' WHEN 'TDB' THEN 'ClickHouse' ELSE 'MariaDB' END AS motor,
       a.num_empleado,
       CONCAT(a.nombre, ' ', a.apellidos) AS ahorrador,
       a.estado,
       COUNT(*) AS prestamos,
       SUM(p.monto) AS monto_total,
       MIN(p.fecha_autorizacion) AS primer_prestamo
  FROM prestamo p
  JOIN ahorrador a ON a.id = p.ahorrador_id
  JOIN caja c ON c.id = a.caja_id
 WHERE p.estado IN ('VIGENTE', 'LIQUIDADO')
   AND p.fecha_autorizacion >= DATE '{{anio}}-01-01' AND p.fecha_autorizacion < DATE '{{anio_sig}}-01-01'
 GROUP BY c.nombre, c.prefijo, a.num_empleado, a.nombre, a.apellidos, a.estado
 ORDER BY c.nombre, a.num_empleado

-- @consulta 5
SELECT c.nombre AS caja,
       CASE c.prefijo WHEN 'ACE' THEN 'PostgreSQL' WHEN 'TDB' THEN 'ClickHouse' ELSE 'MariaDB' END AS motor,
       a.num_empleado,
       CONCAT(a.nombre, ' ', a.apellidos) AS ahorrador,
       a.estado,
       COALESCE((SELECT SUM(d.interes_asignado)
                   FROM ciclo_distribucion d JOIN ciclo ci ON ci.id = d.ciclo_id
                  WHERE d.ahorrador_id = a.id AND ci.anio = {{anio}}), 0) AS utilidad,
       COALESCE(SUM(CASE WHEN m.tipo IN ('AHORRO_ORDINARIO', 'AHORRO_VOLUNTARIO') THEN m.monto ELSE 0 END), 0) AS ahorro_aportado,
       COALESCE(SUM(CASE WHEN m.tipo IN ('ABONO_CAPITAL', 'ABONO_INTERES') THEN m.monto ELSE 0 END), 0) AS abonos_prestamo,
       COALESCE(SUM(CASE WHEN m.tipo = 'DESEMBOLSO_PRESTAMO' THEN m.monto ELSE 0 END), 0) AS prestamos_recibidos,
       COALESCE(SUM(CASE WHEN m.tipo = 'INTERES_ANUAL' THEN m.monto ELSE 0 END), 0) AS rendimiento_pagado,
       COALESCE(SUM(CASE WHEN m.tipo = 'LIQUIDACION_CICLO' THEN m.monto ELSE 0 END), 0) AS liquidaciones,
       COALESCE(SUM(CASE WHEN m.tipo IN ('AHORRO_ORDINARIO', 'AHORRO_VOLUNTARIO', 'ABONO_CAPITAL', 'ABONO_INTERES') THEN m.monto
                         ELSE -m.monto END), 0) AS balance
  FROM ahorrador a
  JOIN caja c ON c.id = a.caja_id
  LEFT JOIN movimiento m ON m.ahorrador_id = a.id
                        AND m.fecha >= DATE '{{anio}}-01-01' AND m.fecha < DATE '{{anio_sig}}-01-01'
 WHERE a.fecha_ingreso < DATE '{{anio_sig}}-01-01'
   AND (a.fecha_baja IS NULL OR a.fecha_baja >= DATE '{{anio}}-01-01')
 GROUP BY a.id, c.nombre, c.prefijo, a.num_empleado, a.nombre, a.apellidos, a.estado
 ORDER BY c.nombre, a.num_empleado

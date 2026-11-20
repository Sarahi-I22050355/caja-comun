-- Comparación antes/después de los índices de la migración IndicesRendimiento.
-- "Antes" = la misma consulta ignorando el índice nuevo (IGNORE INDEX). ANALYZE ejecuta la consulta y
-- reporta filas leídas reales (r_rows). Se corre con el perfil full: scripts/explain-indices.ps1
SET @caja := (SELECT id FROM caja WHERE prefijo = 'GIM');
SET @ahorrador := (SELECT id FROM ahorrador WHERE num_empleado = 'GIM-00123');
SET @periodo := (SELECT id FROM periodo WHERE caja_id = @caja AND anio = 2024 AND numero = 20);

SELECT '--- C1. Estado de cuenta: movimientos de un ahorrador en el ciclo (antes)' AS consulta;
ANALYZE SELECT fecha, tipo, monto, referencia
  FROM movimiento IGNORE INDEX (ix_movimiento_ahorrador_id_fecha)
 WHERE ahorrador_id = @ahorrador AND fecha BETWEEN '2024-01-01' AND '2024-12-31' ORDER BY fecha;
SELECT '--- C1. (después)' AS consulta;
ANALYZE SELECT fecha, tipo, monto, referencia
  FROM movimiento
 WHERE ahorrador_id = @ahorrador AND fecha BETWEEN '2024-01-01' AND '2024-12-31' ORDER BY fecha;

SELECT '--- C2. Nómina: movimientos de una caja en un periodo (antes)' AS consulta;
ANALYZE SELECT ahorrador_id, tipo, monto
  FROM movimiento IGNORE INDEX (ix_movimiento_caja_id_periodo_id)
 WHERE caja_id = @caja AND periodo_id = @periodo;
SELECT '--- C2. (después)' AS consulta;
ANALYZE SELECT ahorrador_id, tipo, monto
  FROM movimiento
 WHERE caja_id = @caja AND periodo_id = @periodo;

SELECT '--- C3. Estado de resultados: intereses cobrados por rango de fechas (antes)' AS consulta;
ANALYZE SELECT caja_id, SUM(monto)
  FROM movimiento IGNORE INDEX (ix_movimiento_tipo_fecha)
 WHERE tipo = 'ABONO_INTERES' AND fecha BETWEEN '2023-01-01' AND '2023-12-31' GROUP BY caja_id;
SELECT '--- C3. (después)' AS consulta;
ANALYZE SELECT caja_id, SUM(monto)
  FROM movimiento
 WHERE tipo = 'ABONO_INTERES' AND fecha BETWEEN '2023-01-01' AND '2023-12-31' GROUP BY caja_id;

SELECT '--- C4. Padrón: ahorradores activos de una caja ordenados por nombre, primera página (antes)' AS consulta;
ANALYZE SELECT id, num_empleado, apellidos, nombre
  FROM ahorrador IGNORE INDEX (ix_ahorrador_caja_id_apellidos_nombre, ix_ahorrador_caja_id_estado)
 WHERE caja_id = @caja AND estado = 'ACTIVO' ORDER BY apellidos, nombre LIMIT 50;
SELECT '--- C4. (después)' AS consulta;
ANALYZE SELECT id, num_empleado, apellidos, nombre
  FROM ahorrador
 WHERE caja_id = @caja AND estado = 'ACTIVO' ORDER BY apellidos, nombre LIMIT 50;

SELECT '--- C5. Cartera: préstamos vigentes de una caja (antes)' AS consulta;
ANALYZE SELECT folio, ahorrador_id, monto
  FROM prestamo IGNORE INDEX (ix_prestamo_caja_id_estado)
 WHERE caja_id = @caja AND estado = 'VIGENTE';
SELECT '--- C5. (después)' AS consulta;
ANALYZE SELECT folio, ahorrador_id, monto
  FROM prestamo
 WHERE caja_id = @caja AND estado = 'VIGENTE';

SELECT '--- C6. Cuotas por descontar en un periodo (antes)' AS consulta;
ANALYZE SELECT prestamo_id, total
  FROM prestamo_amortizacion IGNORE INDEX (ix_prestamo_amortizacion_periodo_id_estado)
 WHERE periodo_id = @periodo AND estado = 'APLICADO';
SELECT '--- C6. (después)' AS consulta;
ANALYZE SELECT prestamo_id, total
  FROM prestamo_amortizacion
 WHERE periodo_id = @periodo AND estado = 'APLICADO';

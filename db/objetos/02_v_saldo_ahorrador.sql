-- Ahorro acumulado del ciclo en curso: solo cuentan las aportaciones posteriores a la última liquidación.
-- La fecha de la última liquidación se busca con una subconsulta por ahorrador (usa el índice
-- ahorrador_id, fecha) en lugar de una tabla derivada: así la vista se puede unir a la lista de
-- ahorradores sin recalcularse por cada renglón (ver docs/indices.md, C7).
CREATE VIEW v_saldo_ahorrador AS
SELECT a.id AS ahorrador_id,
       a.caja_id,
       CAST(COALESCE(SUM(CASE WHEN m.tipo = 'AHORRO_ORDINARIO'  THEN m.monto END), 0) AS DECIMAL(14,2)) AS ordinario,
       CAST(COALESCE(SUM(CASE WHEN m.tipo = 'AHORRO_VOLUNTARIO' THEN m.monto END), 0) AS DECIMAL(14,2)) AS voluntario,
       CAST(COALESCE(SUM(m.monto), 0) AS DECIMAL(14,2)) AS total
  FROM ahorrador a
  LEFT JOIN movimiento m
         ON m.ahorrador_id = a.id
        AND m.tipo IN ('AHORRO_ORDINARIO', 'AHORRO_VOLUNTARIO')
        AND m.fecha > COALESCE((SELECT MAX(l.fecha)
                                  FROM movimiento l
                                 WHERE l.ahorrador_id = a.id AND l.tipo = 'LIQUIDACION_CICLO'), '1000-01-01')
 GROUP BY a.id, a.caja_id

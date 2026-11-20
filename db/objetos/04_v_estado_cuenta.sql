-- Movimientos con su efecto sobre el ahorro y el saldo de ahorro acumulado renglón por renglón.
CREATE VIEW v_estado_cuenta AS
SELECT m.id AS movimiento_id,
       m.caja_id,
       m.ahorrador_id,
       m.periodo_id,
       m.prestamo_id,
       m.fecha,
       m.tipo,
       m.monto,
       m.referencia,
       CASE m.tipo
            WHEN 'AHORRO_ORDINARIO'  THEN m.monto
            WHEN 'AHORRO_VOLUNTARIO' THEN m.monto
            WHEN 'LIQUIDACION_CICLO' THEN -m.monto
            ELSE 0
       END AS efecto_ahorro,
       SUM(CASE m.tipo
                WHEN 'AHORRO_ORDINARIO'  THEN m.monto
                WHEN 'AHORRO_VOLUNTARIO' THEN m.monto
                WHEN 'LIQUIDACION_CICLO' THEN -m.monto
                ELSE 0
           END) OVER (PARTITION BY m.ahorrador_id ORDER BY m.fecha, m.id) AS saldo_ahorro
  FROM movimiento m

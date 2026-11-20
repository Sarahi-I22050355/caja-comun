-- Devenga el interés del periodo que contiene la fecha de corte: saldo insoluto al inicio del periodo
-- por la mitad de la tasa mensual (el mismo cálculo de la tabla de amortización). Lo invoca la tarea
-- programada; es idempotente (volver a correrlo para el mismo corte actualiza los renglones).
CREATE PROCEDURE sp_calcular_interes_devengado(IN p_caja_id INT, IN p_fecha_corte DATE)
BEGIN
    DECLARE v_periodo_id INT DEFAULT NULL;
    DECLARE v_inicio DATE;

    SELECT id, fecha_inicio INTO v_periodo_id, v_inicio
      FROM periodo
     WHERE caja_id = p_caja_id
       AND p_fecha_corte BETWEEN fecha_inicio AND fecha_fin
     LIMIT 1;

    IF v_periodo_id IS NULL THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'PERIODO_NO_ENCONTRADO';
    END IF;

    INSERT INTO interes_devengado (prestamo_id, periodo_id, fecha_corte, saldo_insoluto, interes)
    SELECT pr.id, v_periodo_id, p_fecha_corte, x.saldo,
           ROUND(x.saldo * pr.tasa_mensual / 100 / 2, 2)
      FROM prestamo pr
      JOIN (SELECT m.prestamo_id,
                   SUM(CASE
                           WHEN m.tipo = 'DESEMBOLSO_PRESTAMO' AND m.fecha < v_inicio THEN m.monto
                           WHEN m.tipo = 'ABONO_CAPITAL' AND m.fecha < v_inicio THEN -m.monto
                           ELSE 0
                       END) AS saldo
              FROM movimiento m
              JOIN prestamo p2 ON p2.id = m.prestamo_id
             WHERE p2.caja_id = p_caja_id
             GROUP BY m.prestamo_id
            HAVING saldo > 0) x ON x.prestamo_id = pr.id
    ON DUPLICATE KEY UPDATE periodo_id = VALUES(periodo_id),
                            saldo_insoluto = VALUES(saldo_insoluto),
                            interes = VALUES(interes);

    SELECT COUNT(*) AS prestamos_procesados, COALESCE(SUM(interes), 0) AS interes_total
      FROM interes_devengado d
      JOIN prestamo p3 ON p3.id = d.prestamo_id
     WHERE p3.caja_id = p_caja_id AND d.fecha_corte = p_fecha_corte;
END

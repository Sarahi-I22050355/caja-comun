-- Recalcula saldo-días, saldo promedio y días activos de cada ahorrador del ciclo (borrador recalculable).
-- Una aportación cuenta desde su fecha; una liquidación surte efecto al terminar su día. La asignación del
-- interés (método del mayor residuo) la hace la aplicación. Un ciclo cerrado no se puede recalcular.
CREATE PROCEDURE sp_saldo_dias_ciclo(IN p_ciclo_id INT)
BEGIN
    DECLARE v_caja_id INT DEFAULT NULL;
    DECLARE v_estado VARCHAR(20);
    DECLARE v_inicio DATE;
    DECLARE v_fin DATE;
    DECLARE v_total DECIMAL(18,2);

    SELECT caja_id, estado, MAKEDATE(anio, 1), STR_TO_DATE(CONCAT(anio, '-12-31'), '%Y-%m-%d')
      INTO v_caja_id, v_estado, v_inicio, v_fin
      FROM ciclo
     WHERE id = p_ciclo_id;

    IF v_caja_id IS NULL THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'CICLO_NO_ENCONTRADO';
    END IF;
    IF v_estado = 'CERRADO' THEN
        SIGNAL SQLSTATE '45000' SET MESSAGE_TEXT = 'CICLO_CERRADO';
    END IF;

    DELETE FROM ciclo_distribucion WHERE ciclo_id = p_ciclo_id;

    INSERT INTO ciclo_distribucion
        (ciclo_id, ahorrador_id, saldo_dias, saldo_promedio, dias_activos, participacion, interes_asignado, ahorro_liquidado)
    SELECT p_ciclo_id, x.ahorrador_id, x.saldo_dias,
           ROUND(x.saldo_dias / x.dias_activos, 2), x.dias_activos, 0, 0, x.ahorro_al_corte
      FROM (SELECT m.ahorrador_id,
                   SUM(CASE WHEN m.tipo = 'LIQUIDACION_CICLO'
                            THEN -m.monto * DATEDIFF(v_fin, m.fecha)
                            ELSE  m.monto * (DATEDIFF(v_fin, m.fecha) + 1) END) AS saldo_dias,
                   SUM(CASE WHEN m.tipo = 'LIQUIDACION_CICLO' THEN -m.monto ELSE m.monto END) AS ahorro_al_corte,
                   DATEDIFF(COALESCE(MAX(CASE WHEN m.tipo = 'LIQUIDACION_CICLO' THEN m.fecha END), v_fin),
                            MIN(CASE WHEN m.tipo <> 'LIQUIDACION_CICLO' THEN m.fecha END)) + 1 AS dias_activos
              FROM movimiento m
             WHERE m.caja_id = v_caja_id
               AND m.fecha BETWEEN v_inicio AND v_fin
               AND m.tipo IN ('AHORRO_ORDINARIO', 'AHORRO_VOLUNTARIO', 'LIQUIDACION_CICLO')
             GROUP BY m.ahorrador_id
            HAVING MIN(CASE WHEN m.tipo <> 'LIQUIDACION_CICLO' THEN m.fecha END) IS NOT NULL) x;

    SELECT COALESCE(SUM(saldo_dias), 0) INTO v_total FROM ciclo_distribucion WHERE ciclo_id = p_ciclo_id;
    IF v_total > 0 THEN
        UPDATE ciclo_distribucion
           SET participacion = ROUND(saldo_dias / v_total, 6)
         WHERE ciclo_id = p_ciclo_id;
    END IF;

    SELECT COUNT(*) AS ahorradores, v_total AS saldo_dias_total
      FROM ciclo_distribucion WHERE ciclo_id = p_ciclo_id;
END

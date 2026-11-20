-- Saldo de ahorro (ordinario + voluntario - liquidaciones) de un ahorrador a una fecha.
-- El rendimiento anual (INTERES_ANUAL) no forma parte del ahorro acumulado.
CREATE FUNCTION fn_saldo_ahorro(p_ahorrador_id INT, p_fecha DATE)
RETURNS DECIMAL(14,2)
READS SQL DATA
BEGIN
    DECLARE v_saldo DECIMAL(14,2);

    SELECT COALESCE(SUM(CASE tipo
                            WHEN 'AHORRO_ORDINARIO'  THEN monto
                            WHEN 'AHORRO_VOLUNTARIO' THEN monto
                            WHEN 'LIQUIDACION_CICLO' THEN -monto
                            ELSE 0
                        END), 0)
      INTO v_saldo
      FROM movimiento
     WHERE ahorrador_id = p_ahorrador_id
       AND fecha <= p_fecha;

    RETURN v_saldo;
END

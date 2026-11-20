-- Descuento de nómina por ahorrador y periodo: ordinario + voluntario (si está activo) + cuota de préstamo.
-- Una licencia, incapacidad o baja en el periodo suspende todo el descuento; SUSPENDE_VOLUNTARIO solo el voluntario.
-- Conviene filtrar siempre por periodo_id.
CREATE VIEW v_descuento_periodo AS
SELECT t.ahorrador_id,
       t.caja_id,
       t.periodo_id,
       t.suspendido,
       IF(t.suspendido, 0, t.cuota_ordinaria) AS cuota_ordinaria,
       IF(t.suspendido OR t.suspende_voluntario OR NOT t.voluntario_activo, 0, t.cuota_voluntaria) AS cuota_voluntaria,
       IF(t.suspendido, 0, t.cuota_prestamo) AS cuota_prestamo,
       IF(t.suspendido, 0,
          t.cuota_ordinaria
          + IF(t.suspende_voluntario OR NOT t.voluntario_activo, 0, t.cuota_voluntaria)
          + t.cuota_prestamo) AS total
  FROM (
        SELECT a.id AS ahorrador_id,
               a.caja_id,
               p.id AS periodo_id,
               cu.cuota_ordinaria,
               cu.cuota_voluntaria,
               cu.voluntario_activo,
               EXISTS (SELECT 1 FROM incidencia i
                        WHERE i.ahorrador_id = a.id AND i.periodo_id = p.id
                          AND i.tipo IN ('LICENCIA', 'INCAPACIDAD', 'BAJA')) AS suspendido,
               EXISTS (SELECT 1 FROM incidencia i
                        WHERE i.ahorrador_id = a.id AND i.periodo_id = p.id
                          AND i.tipo = 'SUSPENDE_VOLUNTARIO') AS suspende_voluntario,
               COALESCE((SELECT SUM(am.total)
                           FROM prestamo_amortizacion am
                           JOIN prestamo pr ON pr.id = am.prestamo_id
                          WHERE pr.ahorrador_id = a.id
                            AND pr.estado IN ('VIGENTE', 'LIQUIDADO')
                            AND am.periodo_id = p.id), 0) AS cuota_prestamo
          FROM periodo p
          JOIN ahorrador a
            ON a.caja_id = p.caja_id
           AND a.fecha_ingreso <= p.fecha_fin
           AND (a.fecha_baja IS NULL OR a.fecha_baja > p.fecha_fin)
          JOIN cuota_ahorro cu
            ON cu.ahorrador_id = a.id
           AND cu.vigente_desde = (SELECT MAX(c2.vigente_desde)
                                     FROM cuota_ahorro c2
                                    WHERE c2.ahorrador_id = a.id
                                      AND c2.vigente_desde <= p.fecha_fin)
       ) t

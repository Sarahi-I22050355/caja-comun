-- Saldo y avance de cada préstamo a partir de sus movimientos y de la tabla de amortización.
CREATE VIEW v_prestamo_resumen AS
SELECT p.id AS prestamo_id,
       p.folio,
       p.caja_id,
       p.ahorrador_id,
       p.monto,
       p.plazo_quincenas,
       p.tasa_mensual,
       p.estado,
       CAST(COALESCE(mv.capital_pagado, 0) AS DECIMAL(14,2)) AS capital_pagado,
       CAST(COALESCE(mv.interes_pagado, 0) AS DECIMAL(14,2)) AS interes_pagado,
       CAST(CASE WHEN COALESCE(mv.desembolsos, 0) > 0
                 THEN p.monto - COALESCE(mv.capital_pagado, 0)
                 ELSE 0 END AS DECIMAL(14,2)) AS saldo_vigente,
       COALESCE(am.cuotas_aplicadas, 0) AS cuotas_aplicadas,
       p.plazo_quincenas - COALESCE(am.cuotas_aplicadas, 0) AS cuotas_restantes
  FROM prestamo p
  LEFT JOIN (SELECT prestamo_id,
                    SUM(CASE WHEN tipo = 'ABONO_CAPITAL' THEN monto END) AS capital_pagado,
                    SUM(CASE WHEN tipo = 'ABONO_INTERES' THEN monto END) AS interes_pagado,
                    SUM(tipo = 'DESEMBOLSO_PRESTAMO') AS desembolsos
               FROM movimiento
              WHERE prestamo_id IS NOT NULL
              GROUP BY prestamo_id) mv ON mv.prestamo_id = p.id
  LEFT JOIN (SELECT prestamo_id, COUNT(*) AS cuotas_aplicadas
               FROM prestamo_amortizacion
              WHERE estado = 'APLICADO'
              GROUP BY prestamo_id) am ON am.prestamo_id = p.id

# Índices y planes de ejecución

Los índices de la migración `IndicesRendimiento` se justifican con `ANALYZE` de MariaDB (ejecuta la consulta y
reporta las filas leídas de verdad, `r_rows`). "Antes" es la misma consulta con `IGNORE INDEX` sobre el índice nuevo;
"después" es la consulta sin pistas. Datos: perfil `full` (111,469 movimientos, 349 préstamos, 3,444 cuotas).

Script: [`db/explain_indices.sql`](../db/explain_indices.sql) (`scripts/explain-indices.ps1` o `.sh`).

| # | Consulta (pantalla) | Índice | Antes | Después |
|---|---|---|---|---|
| C1 | Movimientos de un ahorrador en el ciclo (estado de cuenta) | `movimiento (ahorrador_id, fecha)` | `ALL`, 111,369 filas estimadas, filesort | `range`, 46 filas leídas, sin filesort |
| C3 | Intereses cobrados por rango de fechas (estado de resultados) | `movimiento (tipo, fecha)` | `ALL`, 111,469 filas leídas para devolver 1,214 | `range`, 1,214 filas leídas |
| C5 | Préstamos vigentes de una caja (cartera) | `prestamo (caja_id, estado)` | `ALL`, 349 filas leídas para devolver 63 | `ref`, 63 filas leídas |
| C6 | Cuotas de un periodo por estado (nómina) | `prestamo_amortizacion (periodo_id, estado)` | `ALL`, 3,444 filas leídas para devolver 67 | `ref`, 67 filas leídas |
| C4 | Padrón: activos de una caja ordenados por nombre, página 1 | `ahorrador (caja_id, apellidos, nombre)` | `range` por número de empleado, 1,248 filas estimadas, filesort | `range`, ya ordenado: sin filesort (53 filas leídas con `LIMIT 50`) |
| C2 | Movimientos de una caja en un periodo (nómina) | `movimiento (caja_id, periodo_id)` | `ref` por `periodo_id`, 1,557 filas | `ref`, 1,557 filas |
| C7 | Padrón con ahorro de cada ahorrador, ordenado por ahorro (`v_saldo_ahorrador` unida a `ahorrador`) | vista sin tabla derivada (usa `movimiento (ahorrador_id, fecha)`) | ≈ 4,750 ms: `LATERAL DERIVED`, la derivada de liquidaciones se recalcula por cada ahorrador | ≈ 175 ms, mismo resultado (0 diferencias) |

## Lectura honesta de los resultados

- **C1, C3, C5, C6** pasan de leer toda la tabla a leer solo lo necesario. Son las consultas de las pantallas de estado
  de cuenta, reportes, cartera y aplicación de nómina; el beneficio crece con el volumen.
- **C4** elimina el ordenamiento en memoria; con 1,248 filas la diferencia es pequeña, pero el padrón paginado ordena
  siempre por nombre, así que el índice evita el filesort a cualquier tamaño.
- **C2** no mejora en estos datos: `periodo_id` ya identifica una sola caja (cada periodo pertenece a una caja), así que el
  índice por `periodo_id` que MariaDB crea para la llave foránea ya es igual de selectivo. Se conserva porque el
  requerimiento lo pide y cubre consultas por caja que abarquen varios periodos, pero es el candidato a eliminar si
  se necesita reducir el costo de escritura de `movimiento` (que es la tabla que más crece).
- En C1 el `r_rows` del plan "antes" (46) no es comparable porque el plan usa filesort; para ese caso se toma la
  estimación `rows` = 111,369, es decir, la tabla completa.

## C7: la vista de saldos (Fase 3)

Al conectar la API apareció un problema que los datos del perfil `demo` no mostraban: `GET /ahorradores` ordenado por
ahorro y `GET /cajas` tardaban ≈ 4.7 s con el perfil `full`. La primera versión de `v_saldo_ahorrador` calculaba la
fecha de la última liquidación con una tabla derivada (`LEFT JOIN (SELECT ... GROUP BY ...) l`); al unir la vista con
`ahorrador`, MariaDB la trataba como `LATERAL DERIVED` y volvía a materializar esa derivada (2,192 renglones) por cada uno de
los 2,200 ahorradores. La definición actual busca esa fecha con una subconsulta por ahorrador que usa
`ix_movimiento_ahorrador_id_fecha`, y se verificó que da exactamente los mismos saldos. La migración
`VistaSaldoAhorradorSinDerivada` la reemplaza en bases que ya tenían la anterior.

Tiempos medidos de punta a punta (API detrás de nginx, perfil `full`, mejor de 5 llamadas): lista de ahorradores 12-20 ms,
con orden por ahorro 160 ms, `/cajas` 160 ms, detalle de un ahorrador 16 ms, movimientos de un ahorrador 9 ms,
estado de cuenta 11 ms, movimientos de todas las cajas sin filtro 99 ms (el conteo recorre `movimiento`).

## C8: nómina, importaciones y préstamos (Fase 4)

Las consultas de la Fase 4 se midieron con el perfil `full` (GIMSA con 1,188 ahorradores, 2,188 tras importar 1,000 altas),
de punta a punta con la API detrás de nginx. **No hizo falta ningún índice nuevo**:

| Operación | Tiempo |
|---|---|
| Descuentos de un periodo (página de 25 sobre `v_descuento_periodo`) | 0.3 s la primera vez, 0.1-0.2 s después |
| Descuentos con búsqueda o solo suspendidos | 0.1 s |
| Layout CSV del periodo (1,496 renglones) | 0.24 s |
| Layout XLSX del mismo periodo | 1.5 s (arma el libro en memoria) |
| Lista de préstamos (25), ordenada por saldo, resumen de cartera | 0.04-0.13 s |
| Ahorro voluntario: lista, solo activos, resumen | 0.12-0.27 s |
| Simular un préstamo | 0.22 s |
| Importación: vista previa de 1,000 renglones / aplicar 1,000 altas | 1.3 s / 1.1 s |
| **Aplicar un periodo con 2,183 ahorradores** (2,559 movimientos) | 0.6 s (0.3 s el siguiente) |

`EXPLAIN` de la vista por periodo: el periodo se resuelve por llave primaria; la cuota vigente, las incidencias y las cuotas
de préstamo usan sus índices únicos (`ux_cuota_ahorro_ahorrador_id_vigente_desde`, `ux_incidencia_ahorrador_id_periodo_id_tipo`,
`ix_prestamo_amortizacion_periodo_id_estado`, `ix_prestamo_ahorrador_id`). La única lectura completa es la de `ahorrador`
(≈ 3,100 renglones): el optimizador la prefiere sobre `ix_ahorrador_caja_id_estado` porque la caja concentra casi toda la
tabla, y a este tamaño cuesta unos milisegundos. Si el padrón creciera por órdenes de magnitud convendría revisar ese plan.

Aplicar el periodo inserta los movimientos con un `INSERT ... SELECT` desde la vista (sin pasar por la memoria de la API) y
solo carga como entidades las cuotas de préstamo del periodo, que son pocas.

## C9: ciclos, reportes y tareas (Fase 5)

Medido con el perfil `full` (GIMSA con 1,211 participantes en el ciclo 2024), de punta a punta. **Tampoco hizo falta un índice**:

| Operación | Tiempo |
|---|---|
| Borrador de distribución del ciclo (`sp_saldo_dias_ciclo` + reparto + retenciones) | 0.4 s |
| **Cierre del ciclo** (2,443 movimientos), como trabajo en segundo plano | 0.7 s |
| Distribución de 1,215 renglones en PDF / en Excel | 0.64 s / 0.63 s |
| Archivo de dispersión SPEI de 1,215 renglones | 0.03-0.10 s |
| Estado de cuenta de un ahorrador en PDF | 0.06 s |
| Estado de resultados (frío / en caché) | 0.10 s / 0.013 s |
| Dashboard de una caja (frío / en caché) | 0.43 s / 0.015 s |
| Ejecutar la tarea de cálculo de interés (3 cajas) | 0.14 s |

El dashboard en frío es lo más lento: tres agregados sobre `movimiento` (serie de 12 meses, interés del ciclo y último
movimiento). Se resuelve con el caché de Redis (versionado por caja) y queda por debajo de los 500 ms de la meta aun sin él.

## Reglas que se siguieron

- Cada llave foránea tiene un índice cuyo prefijo la cubre (MariaDB no permite soltar el índice que respalda una
  llave foránea; por eso la migración crea los nuevos antes de eliminar los anteriores).
- Los índices compuestos ponen primero la columna de igualdad y después la de rango u orden.
- Las consultas piden solo las columnas necesarias (proyección a DTO) para que el índice pueda cubrirlas.
- No hay N+1: las pantallas de lista usan una consulta con `JOIN` o una vista, nunca una consulta por renglón.

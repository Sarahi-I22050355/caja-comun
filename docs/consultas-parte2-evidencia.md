# Parte 2: evidencia de equivalencia

Resultado de scripts/federacion.ps1 verificar el 2026-11-19 15:12 (hora local).
Cada consulta federada (docs/consultas-parte2.sql, Trino sobre MariaDB + PostgreSQL + ClickHouse) se compara fila por fila
con la misma consulta directa sobre MariaDB (docs/consultas-parte2-mariadb.sql). "idéntico" significa que ambos
conjuntos de filas son exactamente iguales, con las mismas columnas. Los tiempos son los medidos en esta corrida.

| Motor | Cajas | Ahorradores | Movimientos |
|---|---|---:|---:|
| PostgreSQL | Caja de Ahorro Aceros del Norte | 25 | 1381 |
| MariaDB | Caja de Ahorro GIMSA | 60 | 3032 |
| ClickHouse | Caja de Ahorro Transportes del Bravo | 15 | 847 |

| Consulta | Año | Filas federada | Filas MariaDB | Resultado | Federada (ms) | MariaDB (ms) |
|---|---|---:|---:|---|---:|---:|
| Q1 | — | 100 | 100 | idéntico | 372 | 16 |
| Q2 | — | 7 | 7 | idéntico | 243 | 1 |
| Q3 | — | 100 | 100 | idéntico | 261 | 1 |
| Q4 | 2023 | 9 | 9 | idéntico | 469 | 3 |
| Q4 | 2024 | 15 | 15 | idéntico | 513 | 1 |
| Q5 | 2023 | 96 | 96 | idéntico | 2239 | 40 |
| Q5 | 2024 | 100 | 100 | idéntico | 724 | 17 |

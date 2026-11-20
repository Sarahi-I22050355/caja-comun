# Parte 2: consultas distribuidas

El enunciado pide listar, uniendo **todos los motores de base de datos utilizados**: (1) los ahorradores con su estatus y su caja,
(2) los dados de baja, (3) los ahorradores con su edad, (4) los que tienen préstamos en un año X y (5) los que estuvieron en
un año X con su utilidad y el balance de sus movimientos. Se resuelve con **Trino** como motor SQL distribuido.

## Cómo está armado

```
                         API (.NET)  ──  /api/v1/consultas-distribuidas/{q}
                              │              (HttpClient + Polly, protocolo REST de Trino)
                              ▼
                           Trino 484  ───────────────┬──────────────────────┐
                              │                      │                      │
                       catálogo mariadb       catálogo postgresql    catálogo clickhouse
                              │                      │                      │
                  MariaDB 11 (caja_comun)     PostgreSQL 17 (aceros)   ClickHouse 26.10 (bravo)
                  GIMSA + caja + empresa      Aceros del Norte         Transportes del Bravo
```

| Caja | Prefijo | Motor | Por qué ahí |
|---|---|---|---|
| Caja de Ahorro GIMSA | `GIM` | MariaDB | Es la caja con más ahorradores y movimientos y la que opera el sistema: se queda en la fuente de la verdad. También guarda el catálogo de cajas y empresas contra el que se unen las demás. |
| Caja de Ahorro Aceros del Norte | `ACE` | PostgreSQL | Una empresa con un RDBMS transaccional distinto. |
| Caja de Ahorro Transportes del Bravo | `TDB` | ClickHouse | A esa caja se le pide historial de movimientos: lectura analítica sobre una tabla que solo crece, que es lo que ClickHouse hace bien. |

**Qué es real y qué es simulado.** El sistema (Parte 1) escribe únicamente en MariaDB. Las cajas de Aceros y Transportes se
**copian** a PostgreSQL y ClickHouse con un proceso de carga, para tener motores distintos con los datos reales de cada caja.
No es replicación en vivo: hay que volver a cargar cuando cambian los datos. A PostgreSQL y ClickHouse **no se copian RFC, CLABE ni
sueldos**, porque ninguna consulta los necesita.

## Cómo correrlo

Es opcional y no se levanta con `scripts/up`. Con el perfil demo usa unos 1.2 GB de memoria en total (medido: Trino ≈ 1.0 GB,
ClickHouse ≈ 0.1 GB, PostgreSQL ≈ 0.04 GB; los límites son 1.5 GB, 768 MB y 256 MB).

```powershell
scripts/federacion.ps1 iniciar      # levanta Trino, PostgreSQL y ClickHouse, crea el usuario de solo lectura y carga los datos
scripts/federacion.ps1 verificar    # corre las 5 consultas federadas, las compara con MariaDB y escribe la evidencia
scripts/federacion.ps1 cargar       # vuelve a copiar las cajas (después de cambiar datos)
scripts/federacion.ps1 detener
```
En Debian: `scripts/federacion.sh` con los mismos argumentos. Equivale a `docker compose --profile federation up -d` más la
carga. La primera vez descarga unos 2.5 GB de imágenes (Trino es la mayor).

La pantalla **Consultas distribuidas** (`/consultas`) muestra el estado de cada motor (conteos reales), el SQL que se ejecuta en
Trino, los resultados paginados con el total real, la descarga en CSV y el botón **Comparar con MariaDB**. Sin la federación
encendida muestra cómo levantarla.

## Las consultas

El SQL de Trino está en [consultas-parte2.sql](consultas-parte2.sql) y su equivalente directo sobre MariaDB en
[consultas-parte2-mariadb.sql](consultas-parte2-mariadb.sql). Son los mismos archivos que ejecuta la API. Cada una arma un
`WITH` con una rama por motor (`UNION ALL`) y lo une con el catálogo de cajas y empresas de MariaDB: es una unión entre
catálogos, no tres consultas pegadas.

| | Qué devuelve | Criterio |
|---|---|---|
| Q1 | Todos los ahorradores con estado, caja, empresa y el motor donde vive | Sin filtro |
| Q2 | Ahorradores dados de baja, con fecha de baja y días que estuvieron | `estado = 'BAJA'` |
| Q3 | Ahorradores con su edad | Años cumplidos a la fecha de hoy (hora de la Ciudad de México) |
| Q4 | Ahorradores con préstamos en el año X: cuántos, monto total y el primero | Préstamos **otorgados** (vigentes o liquidados) con fecha de autorización en el año |
| Q5 | Ahorradores del año X con su **utilidad** y el **balance** de sus movimientos | Ver abajo |

**Q5.** Ahorradores que estuvieron en la caja en el año: ingresaron antes de que acabara y no se dieron de baja antes de que empezara.
*Utilidad* es el rendimiento que les asignó el cierre de ciclo de ese año (0 si el ciclo aún no se cierra). *Balance* es la suma de
los movimientos del año: **entradas** (ahorro ordinario y voluntario, abonos de capital y de interés) menos **salidas** (desembolsos de
préstamo, rendimiento pagado y liquidaciones de ciclo). Las partes se muestran por separado para poder revisarlo.

Los parámetros (`{{anio}}`, `{{anio_sig}}`, `{{fecha_corte}}`) los sustituye la API con enteros y una fecha generados por ella, nunca
con texto del usuario. Para correrlas a mano: `docker compose --profile federation exec trino trino` y pegar la consulta con los valores puestos.

## Equivalencia con MariaDB

`scripts/federacion.ps1 verificar` ejecuta cada consulta federada y la directa sobre MariaDB y compara el resultado **fila por fila**
(como conjunto y con repeticiones; importes a dos decimales y fechas AAAA-MM-DD, para comparar el valor y no su representación).
La evidencia queda en [consultas-parte2-evidencia.md](consultas-parte2-evidencia.md) con los años 2023 y 2024. La API ofrece lo mismo por
consulta en `GET /consultas-distribuidas/{q}/equivalencia`.

## Detalles que costaron trabajo

- **Fechas anteriores a 1970 en ClickHouse.** El conector de Trino solo acepta `Date` entre 1970 y 2149; hay ahorradores nacidos antes.
  `fecha_nacimiento` se guarda ahí como texto `AAAA-MM-DD` y la rama de ClickHouse la convierte con `CAST(... AS date)`.
- **Conector de ClickHouse.** Con la compresión del driver JDBC activa falla con `Magic is not correct`: la URL lleva `compress=0`.
  `String` se mapea como `varchar` (por omisión Trino lo ve como `varbinary`).
- **Un ahorrador, una fila.** Q5 une movimientos y utilidades por ahorrador; se agrega antes de unir para que un préstamo con
  varios movimientos no multiplique filas.
- **Enteros que salían como importes.** Los conteos de Trino llegaban como `3.00`; hay una prueba de regresión.

## Límites

- Los datos de PostgreSQL y ClickHouse son una copia: tras cambiar datos en el sistema hay que volver a ejecutar `cargar`.
- Las cajas nuevas que se den de alta se quedan en MariaDB (las consultas excluyen solo `ACE` y `TDB` de esa rama). Para mover otra
  caja a un motor hay que agregar su prefijo a la distribución (`DistribucionMotores`) y a las consultas.
- Trino no tiene autenticación: solo escucha en `127.0.0.1` del equipo y dentro de la red de compose. No lo expongas a internet.
- Las consultas abarcan todas las cajas: las ve quien tiene acceso a todas.

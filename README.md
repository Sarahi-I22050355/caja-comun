# Caja Común

Sistema multiempresa de Caja de Ahorro (proyecto final de Tópicos de Bases de Datos).
Autora: Sarahi Reyes Vázquez, Ingeniería Informática, TecNM Campus Monclova.

Stack: MariaDB 11, Redis 7, API ASP.NET Core (.NET 10) con EF Core, Dapper y Quartz, React 18 + TypeScript + Tailwind, nginx.
Parte 2 (opcional): Trino sobre MariaDB, PostgreSQL y ClickHouse.

## Qué hace
- **Captación y nómina**: altas y bajas por archivo (.xlsx, .xml, .txt, .csv), incidencias, descuento quincenal o semanal, layouts para nómina y ahorro voluntario aparte del obligatorio.
- **Préstamos internos**: tope del 80 % del ahorro o pagaré con aval, interés social del 1 % al 2 % mensual sobre saldos insolutos, amortización por nómina.
- **Cierre de ciclo**: interés cobrado, reparto proporcional por saldo y días, y layout bancario SPEI.
- **Reportes** (JSON, PDF y Excel), panel, tareas programadas configurables desde la interfaz, y seguridad por rol y por caja.
- **Interfaz** en rosa, en español e inglés (el cambio de idioma no pierde lo escrito), con 15 pantallas.
- **Parte 2**: cinco consultas SQL distribuidas entre tres motores distintos, con evidencia de equivalencia.

## Requisitos
- Docker Desktop (Compose v2) o Docker Engine en Debian
- .NET SDK 10 y Node 22 o superior (solo para desarrollar fuera de Docker)

## Puesta en marcha (de cero)
```powershell
git clone <repositorio> caja-comun; cd caja-comun
scripts/up.ps1          # crea .env con contraseñas aleatorias y levanta mariadb, redis, api y web
scripts/seed.ps1        # migraciones y datos de demostración; imprime las contraseñas de los usuarios
```
Abrir **http://localhost:8080**. En Debian: `bash scripts/up.sh` y `bash scripts/seed.sh` (o `chmod +x scripts/*.sh` y se ejecutan directo). Sin esos scripts:
`cp .env.example .env` (revisar contraseñas; `JWT_SECRET` no puede quedar vacío) y
`docker compose --env-file .env -f infra/docker-compose.yml up -d --build`.

- Estado de la API: http://localhost:8080/api/health (liveness) y `/api/health/ready` (MariaDB y Redis)
- MariaDB queda en `127.0.0.1:3307` (el 3306 queda libre para un MariaDB local); solo el equipo la alcanza

## Datos de prueba
```powershell
scripts/seed.ps1                  # aplica migraciones, siembra el perfil demo (≈100 ahorradores) y verifica
scripts/seed.ps1 --perfil full    # ≈2,200 ahorradores y ≈111,000 movimientos
scripts/seed.ps1 --reiniciar      # borra todo y vuelve a sembrar
```
En Debian: `scripts/seed.sh` con los mismos argumentos. La semilla es fija: siempre resultan los mismos datos.
Incluye el ciclo 2023 completo (cerrado, con su distribución y su layout SPEI) y 2024 hasta la quincena Q20.
Las contraseñas de los usuarios de demostración se toman de `SEED_PASSWORD_*` en `.env`; si están vacías se asignan
al azar y se imprimen una sola vez al terminar. Usuarios: `admin@cajacomun.mx` (Administrador), `mariana.trevino@cajacomun.mx`
(Capturista, GIMSA), `luis.garza@cajacomun.mx` (Consulta, Aceros del Norte) y `ana.ruiz@cajacomun.mx` (Capturista, Transportes del Bravo).

Aplicar migraciones pendientes sin borrar datos: `scripts/migrar.ps1` (o `.sh`).
Verificar sin volver a sembrar: `docker compose --env-file .env -f infra/docker-compose.yml run --rm api verificar-seed`.
Exportar el esquema a `db/schema.sql`: `scripts/exportar-schema.ps1` (o `.sh`). Planes de ejecución: `scripts/explain-indices.ps1`.

## API
Rutas bajo `/api/v1`, descritas en [docs/api.md](docs/api.md) (roles, parámetros y códigos de error). Ejemplo con el
usuario de demostración:
```powershell
$sesion = Invoke-RestMethod -Method Post http://localhost:8080/api/v1/auth/login -ContentType 'application/json' `
    -Body (@{ correo = 'admin@cajacomun.mx'; password = '<la de SEED_PASSWORD_ADMIN>' } | ConvertTo-Json)
Invoke-RestMethod http://localhost:8080/api/v1/cajas -Headers @{ Authorization = "Bearer $($sesion.accessToken)" }
```

## Parte 2: consultas distribuidas (opcional)
Cinco consultas SQL que Trino resuelve uniendo MariaDB (GIMSA), PostgreSQL (Aceros del Norte) y ClickHouse (Transportes del Bravo).
No se levantan con `scripts/up`: usan unos 1.2 GB más de memoria y descargan ≈2.5 GB de imágenes la primera vez.
```powershell
scripts/federacion.ps1 iniciar      # levanta los tres motores, crea el usuario de solo lectura y carga los datos
scripts/federacion.ps1 verificar    # compara las 5 consultas federadas contra MariaDB y escribe docs/consultas-parte2-evidencia.md
```
En Debian: `scripts/federacion.sh`. Detalle, SQL y evidencia en [docs/consultas-parte2.md](docs/consultas-parte2.md); la pantalla es
`/consultas`.

## Pruebas
```powershell
scripts/test.ps1               # unitarias y de API sin base, dentro de un contenedor
scripts/test-integracion.ps1   # contra un MariaDB desechable (Testcontainers); necesita Docker
```
Las primeras cubren las reglas de negocio (amortización, límite del 80% y aval, descuento de nómina, periodos, reparto por
mayor residuo, CLABE), los validadores, los lectores de archivos, los layouts, los tokens, la autorización por rol de la API, el cliente de
Trino y el SQL de la Parte 2. Las de integración ejecutan contra MariaDB real el aplicado de periodos (idempotencia y concurrencia), las
importaciones, los préstamos, el ahorro voluntario, el cierre de ciclo (con cierres simultáneos), la dispersión SPEI, los reportes, las tareas
con Quartz y los permisos por caja a través de HTTP.
Frontend: `cd web; npm ci; npm test; npm run build` (ver [docs/frontend.md](docs/frontend.md)). Las pruebas de la interfaz cubren los componentes
base (tabla con sus estados, modal), el formato de importes y fechas y que español e inglés tengan las mismas claves.
Recorrido manual de las 15 pantallas: [docs/pruebas-manuales.md](docs/pruebas-manuales.md).

## Operación
```powershell
scripts/respaldo.ps1                          # respaldo comprimido en respaldos/ (restaurar: scripts/respaldo.ps1 restaurar <archivo>)
scripts/medir-api.ps1 -Password '<admin>'     # latencia p50/p95 de los endpoints principales; marca los que pasan de 500 ms
```
Despliegue en un servidor Debian y exposición con Cloudflare Tunnel: [docs/despliegue.md](docs/despliegue.md)
(`docker compose -f infra/docker-compose.yml -f infra/docker-compose.prod.yml up -d --build`).
Las solicitudes de la API que tardan más de 500 ms dejan un aviso `Solicitud lenta` en el log, y hay trazas OpenTelemetry (ver [docs/rendimiento.md](docs/rendimiento.md)).

## Estructura
```
src/      API (Api), casos de uso (Application), reglas de negocio (Domain), acceso a datos y servicios (Infrastructure)
tests/    pruebas xUnit
web/      React + TypeScript + Tailwind (pantallas en src/modulos, textos es/en por módulo)
infra/    docker-compose (y el de producción), Dockerfiles, nginx, cloudflared de ejemplo y la configuración de la federación
db/       esquema exportado (schema.sql), vistas, función y procedimientos (objetos/)
scripts/  levantar, sembrar, probar, respaldar, medir y operar la federación (.ps1 para Windows y .sh para Debian)
docs/     ver abajo
```

## Documentación (docs/)
| Documento | Contenido |
|---|---|
| [decisiones.md](docs/decisiones.md) | Decisiones y supuestos por fase |
| [api.md](docs/api.md) | Rutas, roles, parámetros y códigos de error |
| [modelo-er.md](docs/modelo-er.md), [indices.md](docs/indices.md) | Modelo de datos e índices con `EXPLAIN` |
| [layout-nomina.md](docs/layout-nomina.md), [importacion.md](docs/importacion.md), [layout-spei.md](docs/layout-spei.md) | Archivos de nómina, importación de altas y bajas, dispersión bancaria |
| [frontend.md](docs/frontend.md) | Estructura de la interfaz, idioma y roles |
| [consultas-parte2.md](docs/consultas-parte2.md) | Parte 2: motores, consultas, equivalencia (con [el SQL](docs/consultas-parte2.sql) y [la evidencia](docs/consultas-parte2-evidencia.md)) |
| [rendimiento.md](docs/rendimiento.md) | Qué se hizo para el rendimiento y cómo se comprobó |
| [seguridad.md](docs/seguridad.md) | Controles de seguridad y su revisión |
| [despliegue.md](docs/despliegue.md) | Servidor Debian, Cloudflare Tunnel, respaldos |
| [pruebas-manuales.md](docs/pruebas-manuales.md) | Recorrido de las 15 pantallas en ES y EN |

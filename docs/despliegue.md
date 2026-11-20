# Despliegue

Una sola URL pública para toda la aplicación: nginx sirve React y reenvía `/api` a la API, y un túnel de Cloudflare apunta a
`http://localhost:8080`. Sirve igual en Windows y en un servidor Debian.

## Local (desarrollo o demostración)

```powershell
git clone <repositorio> caja-comun; cd caja-comun
scripts/up.ps1                  # crea .env con contraseñas aleatorias y levanta mariadb, redis, api y web
scripts/seed.ps1                # migraciones y datos de demostración (≈100 ahorradores); imprime las contraseñas de los usuarios
```
Abrir `http://localhost:8080`. En Debian: `scripts/up.sh` y `scripts/seed.sh`. MariaDB queda en `127.0.0.1:3307` (solo el equipo).

## Producción en un servidor Debian

Requisitos: `git`, 2 GB de RAM libres (4 GB si se enciende la federación de la Parte 2) y **Docker Engine con el plugin Compose v2.24 o más nuevo**
(instálalo del repositorio de Docker, `docker-ce` y `docker-compose-plugin`; el paquete `docker-compose` de los repositorios de Debian es muy viejo y
no entiende `docker-compose.prod.yml`). Comprueba con `docker compose version`.

```bash
git clone <repositorio> /opt/caja-comun && cd /opt/caja-comun
cp .env.example .env
# Edita .env: contraseñas largas y distintas para MARIADB_ROOT_PASSWORD y MARIADB_PASSWORD, y JWT_SECRET de 48+ caracteres
# (por ejemplo `openssl rand -base64 36`; los tres deben tener valor). Cambiar JWT_SECRET cierra todas las sesiones abiertas.
# Si defines SEED_PASSWORD_* aquí, los usuarios de demostración usarán esas contraseñas (ponlas fuertes si el sitio será público).
docker compose --env-file .env -f infra/docker-compose.yml -f infra/docker-compose.prod.yml up -d --build
bash scripts/seed.sh            # la primera vez: crea las tablas y los usuarios (sin esto no hay con quién entrar)
```
Si al correr un script dice *Permission denied*: `chmod +x scripts/*.sh`, o ejecútalo con `bash scripts/<nombre>.sh`. En producción usa el `docker compose ... -f infra/docker-compose.prod.yml` de arriba y no `scripts/up.sh`: `up.sh` levanta la versión de desarrollo (nginx abierto a toda la red).

Qué cambia `docker-compose.prod.yml` respecto del compose normal:
- **nginx escucha solo en `127.0.0.1:8080`**: lo único que debe alcanzarlo es el túnel (o un proxy local). Así nadie puede mandar una
  cabecera `CF-Connecting-IP` falsa saltándose Cloudflare, que es de donde la API toma la IP para limitar los intentos de inicio de sesión.
- **MariaDB no publica su puerto**: solo la API la alcanza por la red interna.
- Reinicio automático y registros con rotación (3 archivos de 10 MB por contenedor).

**Datos persistentes.** MariaDB, PostgreSQL y ClickHouse usan volúmenes con nombre (`mariadb_data`, `postgres_data`, `clickhouse_data`):
sobreviven a `docker compose down` y a reconstruir imágenes. **No uses `down -v`**: borra los volúmenes.

**Actualizar.** `git pull`, repetir el `up -d --build` y aplicar las migraciones pendientes sin tocar los datos con `scripts/migrar.sh`
(`scripts/migrar.ps1` en Windows). La API no migra sola al arrancar.

## Cloudflare Tunnel

Requisito: un dominio en Cloudflare. Con la CLI `cloudflared` instalada (Windows: `winget install Cloudflare.cloudflared`; Debian: paquete `.deb` de Cloudflare):

```bash
cloudflared tunnel login                         # abre el navegador para autorizar el dominio
cloudflared tunnel create caja-comun             # imprime el id y crea el archivo de credenciales (no lo versiones)
cloudflared tunnel route dns caja-comun caja.<tu-dominio>
cp infra/cloudflared.example.yml ~/.cloudflared/config.yml     # y reemplaza <ID-DEL-TUNEL>, <RUTA> y <TU-DOMINIO>
cloudflared tunnel run caja-comun                # o como servicio: cloudflared service install
```
Windows: igual, con la configuración en `%USERPROFILE%\.cloudflared\config.yml` y `cloudflared service install` para dejarlo como servicio.

La aplicación ya está lista para quedar detrás del túnel:
- La API respeta `X-Forwarded-For/Proto/Host` (nginx las pasa) y la cookie de sesión lleva `Secure` cuando la petición original fue HTTPS.
- nginx envía HSTS solo cuando `X-Forwarded-Proto` es `https`, y la política de contenido de la interfaz no permite recursos de otros orígenes.
- En Cloudflare conviene activar *Always Use HTTPS* y dejar el modo SSL en *Full*. Cloudflare comprime los estáticos con Brotli en el borde y
  puede cachear `/assets/*` (llevan hash en el nombre).

## Respaldo y restauración

```bash
scripts/respaldo.sh                              # respaldos/caja_comun-AAAAMMDD-HHMMSS.sql.gz (mysqldump comprimido)
scripts/respaldo.sh restaurar respaldos/<archivo>   # reemplaza los datos actuales
```
En Windows: `scripts/respaldo.ps1` con los mismos argumentos. El respaldo usa `--single-transaction` (consistente sin bloquear), e incluye
procedimientos, funciones y vistas. Se comprobó restaurándolo en una base temporal: mismos 5,260 movimientos y las 3 rutinas.
Programa uno diario con `cron` y copia la carpeta `respaldos/` fuera del servidor; no se versiona.

Para los datos de la federación basta volver a cargar (`scripts/federacion.sh cargar`): son una copia de MariaDB.

## Variables de entorno

Todas en `.env` (ignorado por git); `.env.example` es la plantilla. Además, opcionales para la API:

| Variable | Para qué |
|---|---|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | Exporta trazas y métricas por OTLP (por ejemplo `http://collector:4317`) |
| `Telemetria__Consola` | `true` imprime las trazas en consola |
| `Telemetria__UmbralLentoMs` | Umbral para el aviso de solicitud lenta (500 por omisión) |
| `Tareas__Habilitadas` | `false` desactiva el programador de tareas (útil al restaurar un respaldo) |
| `Trino__Url` | Dirección de Trino (la fija el compose) |

## Comprobar que todo vive

```bash
docker compose --env-file .env -f infra/docker-compose.yml ps         # api, mariadb y redis en "healthy"
curl -s http://localhost:8080/api/health/ready                         # {"status":"Healthy", ...}
docker compose --env-file .env -f infra/docker-compose.yml logs api --since 10m | grep "Solicitud lenta"
```

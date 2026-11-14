# Caja Común

Sistema multiempresa de Caja de Ahorro (proyecto final de Tópicos de Bases de Datos).
Autora: Sarahi Reyes Vázquez, Ingeniería Informática, TecNM Campus Monclova.

Stack: MariaDB 11, Redis, API ASP.NET Core (.NET 10), React + TypeScript + Tailwind, nginx.

> Estado: base del repositorio e infraestructura. Los módulos se agregan por fases.

## Requisitos
- Docker Desktop (Compose v2)
- .NET SDK 10 y Node 22 o superior (solo para desarrollar fuera de Docker)

## Puesta en marcha
```powershell
Copy-Item .env.example .env      # revisar contraseñas
docker compose --env-file .env -f infra/docker-compose.yml up -d --build
```
En Debian: `cp .env.example .env` y el mismo `docker compose`. También existen `scripts/up.ps1` y `scripts/up.sh`.

- Aplicación: http://localhost:8080
- Estado de la API: http://localhost:8080/api/health (liveness) y `/api/health/ready` (MariaDB y Redis)
- MariaDB publicada en el puerto **3307** del host (el 3306 queda libre para un MariaDB local)

## Pruebas
```powershell
scripts/test.ps1     # corre las pruebas .NET dentro de un contenedor
```
Frontend: `cd web; npm ci; npm run build`.

## Estructura
```
src/    API (Api), casos de uso (Application), reglas de negocio (Domain), acceso a datos y servicios (Infrastructure)
tests/  pruebas xUnit
web/    React + TypeScript + Tailwind
infra/  docker-compose, Dockerfiles, nginx
db/     esquema SQL, vistas, procedimientos, semillas
docs/   documentación técnica y decisiones
scripts/ utilidades para levantar, probar y sembrar datos
```

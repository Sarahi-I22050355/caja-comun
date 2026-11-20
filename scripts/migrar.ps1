# Aplica las migraciones pendientes a la base de datos del compose (no borra datos).
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
if (-not (Test-Path .env)) { throw 'Falta .env: ejecuta scripts/up.ps1 primero.' }
docker compose --env-file .env -f infra/docker-compose.yml build api
docker compose --env-file .env -f infra/docker-compose.yml run --rm api migrar

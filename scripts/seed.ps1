# Siembra la base de datos (aplica migraciones, carga datos y verifica).
#   scripts/seed.ps1                       perfil demo (≈100 ahorradores)
#   scripts/seed.ps1 --perfil full         perfil completo (≈2,200 ahorradores)
#   scripts/seed.ps1 --reiniciar           borra todo y vuelve a sembrar
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
if (-not (Test-Path .env)) { throw 'Falta .env: ejecuta scripts/up.ps1 primero.' }
docker compose --env-file .env -f infra/docker-compose.yml build api
docker compose --env-file .env -f infra/docker-compose.yml run --rm api seed @args

# Levanta la pila completa. Requiere .env en la raíz (copiar de .env.example).
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
if (-not (Test-Path .env)) { Copy-Item .env.example .env; Write-Host 'Se creó .env desde .env.example; revisa las contraseñas.' }
docker compose --env-file .env -f infra/docker-compose.yml up -d --build

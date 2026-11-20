#!/usr/bin/env bash
# Aplica las migraciones pendientes a la base de datos del compose (no borra datos).
set -euo pipefail
cd "$(dirname "$0")/.."
[ -f .env ] || { echo "Falta .env: ejecuta scripts/up.sh primero." >&2; exit 1; }
docker compose --env-file .env -f infra/docker-compose.yml build api
docker compose --env-file .env -f infra/docker-compose.yml run --rm api migrar

#!/usr/bin/env bash
# Siembra la base de datos (aplica migraciones, carga datos y verifica).
#   scripts/seed.sh                       perfil demo (~100 ahorradores)
#   scripts/seed.sh --perfil full         perfil completo (~2,200 ahorradores)
#   scripts/seed.sh --reiniciar           borra todo y vuelve a sembrar
set -euo pipefail
cd "$(dirname "$0")/.."
[ -f .env ] || { echo "Falta .env: ejecuta scripts/up.sh primero." >&2; exit 1; }
docker compose --env-file .env -f infra/docker-compose.yml build api
docker compose --env-file .env -f infra/docker-compose.yml run --rm api seed "$@"

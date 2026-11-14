#!/usr/bin/env bash
# Levanta la pila completa. Requiere .env en la raíz (copiar de .env.example).
set -euo pipefail
cd "$(dirname "$0")/.."
[ -f .env ] || { cp .env.example .env; echo "Se creó .env desde .env.example; revisa las contraseñas."; }
docker compose --env-file .env -f infra/docker-compose.yml up -d --build

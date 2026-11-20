#!/usr/bin/env bash
# Levanta la pila completa. Si no existe .env lo crea desde .env.example con contraseñas aleatorias.
set -euo pipefail
cd "$(dirname "$0")/.."

nueva_clave() { head -c 256 /dev/urandom | base64 | tr -dc 'A-Za-z0-9' | head -c "${1:-24}"; }

if [ ! -f .env ]; then
  while IFS= read -r linea; do
    case "$linea" in
      MARIADB_ROOT_PASSWORD=|MARIADB_PASSWORD=) echo "${linea}$(nueva_clave)" ;;
      JWT_SECRET=) echo "${linea}$(nueva_clave 48)" ;;
      *) echo "$linea" ;;
    esac
  done < .env.example > .env
  echo "Se creó .env con contraseñas aleatorias."
fi
# Un .env anterior puede no traer JWT_SECRET: se agrega sin tocar el resto.
if ! grep -q '^JWT_SECRET=.' .env; then
  sed -i '/^JWT_SECRET=/d' .env
  printf '\nJWT_SECRET=%s\n' "$(nueva_clave 48)" >> .env
  echo "Se generó JWT_SECRET en .env."
fi
docker compose --env-file .env -f infra/docker-compose.yml up -d --build "$@"

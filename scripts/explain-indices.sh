#!/usr/bin/env bash
# Corre db/explain_indices.sql contra la base del compose (conviene tener el perfil full sembrado).
set -euo pipefail
cd "$(dirname "$0")/.."
docker compose --env-file .env -f infra/docker-compose.yml exec -T mariadb \
  sh -c 'mariadb --table -u"$MARIADB_USER" -p"$MARIADB_PASSWORD" "$MARIADB_DATABASE"' < db/explain_indices.sql

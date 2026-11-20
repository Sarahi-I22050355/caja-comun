#!/usr/bin/env bash
# Exporta db/schema.sql: tablas tal como quedaron en MariaDB (mariadb-dump) y, a continuación, las vistas,
# la función y los procedimientos de db/objetos. Requiere la pila levantada y la base migrada (scripts/seed.sh).
set -euo pipefail
cd "$(dirname "$0")/.."
salida=db/schema.sql
compose=(docker compose --env-file .env -f infra/docker-compose.yml exec -T mariadb)

{
  cat <<'ENCABEZADO'
-- Caja Común - esquema de la base de datos (MariaDB 11, InnoDB, utf8mb4_unicode_ci).
-- Se exporta con scripts/exportar-schema.sh a partir de las migraciones; el sistema crea la base con
-- "dotnet ef database update" / "seed", este archivo es para leerla o cargarla por separado.
-- Las tablas van primero; después las vistas, la función y los procedimientos (db/objetos).

ENCABEZADO

  "${compose[@]}" sh -c '
    tablas=$(mariadb -N -u"$MARIADB_USER" -p"$MARIADB_PASSWORD" "$MARIADB_DATABASE" -e \
      "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE() AND table_type = '"'"'BASE TABLE'"'"' AND table_name <> '"'"'__EFMigrationsHistory'"'"' ORDER BY table_name")
    mariadb-dump -u"$MARIADB_USER" -p"$MARIADB_PASSWORD" --no-data --skip-comments --no-tablespaces --skip-add-drop-table \
      "$MARIADB_DATABASE" $tablas' \
    | tr -d '\r' \
    | sed -e '/^\/\*M!999999/d' -e 's/ AUTO_INCREMENT=[0-9]*//'

  echo
  echo "-- Objetos programables"
  for f in db/objetos/*.sql; do
    echo
    if grep -q -E '^CREATE (FUNCTION|PROCEDURE)' "$f"; then
      echo "DELIMITER ;;"; tr -d '\r' < "$f"; echo ";;"; echo "DELIMITER ;"
    else
      tr -d '\r' < "$f"; echo ";"
    fi
  done
} > "$salida"
echo "Escrito $salida ($(wc -l < "$salida") líneas)"

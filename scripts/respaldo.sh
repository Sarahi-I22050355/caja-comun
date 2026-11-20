#!/usr/bin/env bash
# Respaldo y restauración de la base de datos (mysqldump comprimido).
#   scripts/respaldo.sh                        crea respaldos/caja_comun-AAAAMMDD-HHMMSS.sql.gz
#   scripts/respaldo.sh restaurar <archivo>    carga ese respaldo (¡reemplaza los datos actuales!)
# El dump usa --single-transaction: es consistente sin bloquear la operación. Incluye procedimientos, funciones y vistas.
set -euo pipefail
cd "$(dirname "$0")/.."
dc=(docker compose --env-file .env -f infra/docker-compose.yml)

case "${1:-respaldar}" in
    respaldar)
        mkdir -p respaldos
        archivo="respaldos/caja_comun-$(date +%Y%m%d-%H%M%S).sql.gz"
        "${dc[@]}" exec -T mariadb sh -c 'mariadb-dump -uroot -p"$MARIADB_ROOT_PASSWORD" --single-transaction --routines --events --triggers "$MARIADB_DATABASE" | gzip' > "$archivo"
        echo "Respaldo guardado en $archivo ($(du -h "$archivo" | cut -f1))"
        ;;
    restaurar)
        [ -f "${2:-}" ] || { echo 'Uso: scripts/respaldo.sh restaurar <archivo.sql.gz>' >&2; exit 2; }
        echo "Se reemplazarán los datos actuales con $2."
        gunzip -c "$2" | "${dc[@]}" exec -T mariadb sh -c 'mariadb -uroot -p"$MARIADB_ROOT_PASSWORD" "$MARIADB_DATABASE"'
        echo 'Restaurado. Reinicia la API para limpiar la caché: docker compose restart api'
        ;;
    *) echo 'Uso: scripts/respaldo.sh [restaurar <archivo>]'; exit 2 ;;
esac

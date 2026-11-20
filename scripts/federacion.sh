#!/usr/bin/env bash
# Parte 2: federación de consultas (Trino + MariaDB + PostgreSQL + ClickHouse). Es opcional: con el perfil demo usa unos 1.2 GB de memoria (límite de 2.5 GB entre los tres).
#   scripts/federacion.sh iniciar              levanta los motores, crea el usuario de solo lectura de MariaDB y carga los datos
#   scripts/federacion.sh cargar               vuelve a copiar las cajas desde MariaDB
#   scripts/federacion.sh verificar [--anio N] compara las cinco consultas federadas con MariaDB y escribe docs/consultas-parte2-evidencia.md
#   scripts/federacion.sh detener              apaga los tres contenedores
set -euo pipefail
cd "$(dirname "$0")/.."
accion="${1:-iniciar}"; shift || true
dc=(docker compose --env-file .env -f infra/docker-compose.yml)
dcq=(docker compose --progress quiet --env-file .env -f infra/docker-compose.yml)   # sin el detalle del build

nueva_clave() { LC_ALL=C tr -dc 'A-Za-z0-9' </dev/urandom | head -c 24; }

asegurar_claves() {
    [ -f .env ] || { echo 'No existe .env: corre primero scripts/up.sh.' >&2; exit 1; }
    for n in FEDERACION_MARIADB_PASSWORD FEDERACION_POSTGRES_PASSWORD FEDERACION_CLICKHOUSE_PASSWORD; do
        if ! grep -q "^$n=." .env; then
            { grep -v "^$n=" .env || true; echo "$n=$(nueva_clave)"; } > .env.tmp
            mv .env.tmp .env
            echo "Se generó $n en .env."
        fi
    done
    grep -q '^TRINO_PORT=' .env || echo 'TRINO_PORT=8081' >> .env
}

valor_env() { grep "^$1=" .env | head -n1 | cut -d= -f2-; }

esperar_sano() {
    for _ in $(seq 1 100); do
        id=$("${dc[@]}" --profile federation ps -q "$1")
        if [ -n "$id" ] && [ "$(docker inspect --format '{{.State.Health.Status}}' "$id")" = healthy ]; then return 0; fi
        sleep 3
    done
    echo "$1 no quedó listo. Revisa: docker compose --profile federation logs $1" >&2
    exit 1
}

cargar() { "${dcq[@]}" run --rm --build api cargar-federacion; }

case "$accion" in
    iniciar)
        asegurar_claves
        "${dc[@]}" up -d --build
        pw=$(valor_env FEDERACION_MARIADB_PASSWORD)
        printf "CREATE USER IF NOT EXISTS 'federacion'@'%%' IDENTIFIED BY '%s'; ALTER USER 'federacion'@'%%' IDENTIFIED BY '%s'; GRANT SELECT ON caja_comun.* TO 'federacion'@'%%'; FLUSH PRIVILEGES;" "$pw" "$pw" \
            | "${dc[@]}" exec -T mariadb sh -c 'mariadb -uroot -p"$MARIADB_ROOT_PASSWORD"'
        "${dc[@]}" --profile federation up -d
        esperar_sano trino
        cargar
        echo 'Federación lista. Verifica con: scripts/federacion.sh verificar'
        ;;
    cargar) cargar ;;
    verificar)
        set +e
        salida=$("${dcq[@]}" run --rm --build api verificar-federacion "$@")
        codigo=$?
        set -e
        echo "$salida"
        if [ "$codigo" -eq 0 ] || [ "$codigo" -eq 3 ]; then
            {
                echo '# Parte 2: evidencia de equivalencia'
                echo
                echo "Resultado de scripts/federacion.sh verificar el $(date '+%Y-%m-%d %H:%M') (hora local)."
                echo 'Cada consulta federada (docs/consultas-parte2.sql, Trino sobre MariaDB + PostgreSQL + ClickHouse) se compara fila por fila'
                echo 'con la misma consulta directa sobre MariaDB (docs/consultas-parte2-mariadb.sql). "idéntico" significa que ambos'
                echo 'conjuntos de filas son exactamente iguales, con las mismas columnas. Los tiempos son los medidos en esta corrida.'
                echo
                echo "$salida" | grep -E '^\||^$' || true
            } > docs/consultas-parte2-evidencia.md
            echo 'Evidencia escrita en docs/consultas-parte2-evidencia.md'
        fi
        exit "$codigo"
        ;;
    detener) "${dc[@]}" --profile federation stop trino clickhouse postgres ;;
    *) echo 'Uso: scripts/federacion.sh iniciar | cargar | verificar [--anio N] | detener'; exit 2 ;;
esac

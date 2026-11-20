#!/usr/bin/env bash
# Corre las pruebas de integración (Category=Integracion): usan Testcontainers para levantar un MariaDB desechable,
# así que el contenedor de pruebas necesita el socket de Docker. Tardan unos minutos la primera vez (descarga mariadb:11).
#   scripts/test-integracion.sh                                         todas
#   scripts/test-integracion.sh --filter "FullyQualifiedName~Nomina"    solo las que coincidan
set -euo pipefail
cd "$(dirname "$0")/.."
docker build --target build -t cajacomun-pruebas -f infra/Dockerfile.api .
if [ "$#" -eq 0 ]; then set -- --filter "Category=Integracion"; fi
exec docker run --rm     -v /var/run/docker.sock:/var/run/docker.sock     --add-host host.docker.internal:host-gateway     -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal     -e TESTCONTAINERS_RYUK_DISABLED=true     cajacomun-pruebas dotnet test CajaComun.slnx -c Release --no-build "$@"

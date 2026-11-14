#!/usr/bin/env bash
# Corre las pruebas .NET dentro de un contenedor.
set -euo pipefail
cd "$(dirname "$0")/.."
docker build --target test -f infra/Dockerfile.api .

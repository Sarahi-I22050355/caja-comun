# Corre las pruebas .NET dentro de un contenedor (mismo resultado en Windows y Debian).
# En algunos equipos Windows la directiva de control de aplicaciones bloquea
# los ensamblados de prueba compilados localmente.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
docker build --target test -f infra/Dockerfile.api .

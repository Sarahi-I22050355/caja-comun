# Corre las pruebas de integración (Category=Integracion): usan Testcontainers para levantar un MariaDB desechable,
# así que el contenedor de pruebas necesita el socket de Docker. Tardan unos minutos la primera vez (descarga mariadb:11).
#   scripts/test-integracion.ps1                              todas
#   scripts/test-integracion.ps1 --filter "FullyQualifiedName~Nomina"   solo las que coincidan
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
docker build --target build -t cajacomun-pruebas -f infra/Dockerfile.api .
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$filtro = if ($args.Count -gt 0) { $args } else { @('--filter', 'Category=Integracion') }
docker run --rm `
    -v //var/run/docker.sock:/var/run/docker.sock `
    --add-host host.docker.internal:host-gateway `
    -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal `
    -e TESTCONTAINERS_RYUK_DISABLED=true `
    cajacomun-pruebas dotnet test CajaComun.slnx -c Release --no-build @filtro
exit $LASTEXITCODE

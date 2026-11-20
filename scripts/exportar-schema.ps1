# Exporta db/schema.sql: tablas tal como quedaron en MariaDB y, a continuación, las vistas, la función y los
# procedimientos de db/objetos. Requiere la pila levantada y la base migrada (scripts/seed.ps1).
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$salida = Join-Path (Get-Location) 'db/schema.sql'

$encabezado = @'
-- Caja Común - esquema de la base de datos (MariaDB 11, InnoDB, utf8mb4_unicode_ci).
-- Se exporta con scripts/exportar-schema.ps1 a partir de las migraciones; el sistema crea la base con
-- "dotnet ef database update" / "seed", este archivo es para leerla o cargarla por separado.
-- Las tablas van primero; después las vistas, la función y los procedimientos (db/objetos).

'@

$remoto = @'
tablas=$(mariadb -N -u"$MARIADB_USER" -p"$MARIADB_PASSWORD" "$MARIADB_DATABASE" -e "SELECT table_name FROM information_schema.tables WHERE table_schema = DATABASE() AND table_type = 'BASE TABLE' AND table_name <> '__EFMigrationsHistory' ORDER BY table_name")
mariadb-dump -u"$MARIADB_USER" -p"$MARIADB_PASSWORD" --no-data --skip-comments --no-tablespaces --skip-add-drop-table "$MARIADB_DATABASE" $tablas
exit $?
'@ -replace "`r", ''

# El script viaja en base64: como argumento directo se pierden las comillas en Windows PowerShell y por la
# entrada estándar algunas configuraciones anteponen una marca BOM que rompe la primera línea.
$b64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($remoto))
$volcado = docker compose --env-file .env -f infra/docker-compose.yml exec -T mariadb sh -c "echo $b64 | base64 -d | sh"
if ($LASTEXITCODE -ne 0) { throw 'No se pudo ejecutar mariadb-dump.' }
$tablas = ($volcado | Where-Object { $_ -notmatch '^/\*M!999999' }) -replace ' AUTO_INCREMENT=\d+', ''

$objetos = foreach ($f in Get-ChildItem db/objetos/*.sql | Sort-Object Name) {
    $texto = (Get-Content $f.FullName -Raw -Encoding UTF8).TrimEnd()
    ''
    if ($texto -match '(?m)^CREATE (FUNCTION|PROCEDURE)') { "DELIMITER ;;`n$texto`n;;`nDELIMITER ;" } else { "$texto`n;" }
}

$contenido = $encabezado + ($tablas -join "`n") + "`n`n-- Objetos programables`n" + ($objetos -join "`n") + "`n"
[IO.File]::WriteAllText($salida, $contenido, (New-Object Text.UTF8Encoding($false)))
Write-Host "Escrito $salida"

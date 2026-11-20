# Respaldo y restauración de la base de datos (mysqldump comprimido).
#   scripts/respaldo.ps1                        crea respaldos/caja_comun-AAAAMMDD-HHMMSS.sql.gz
#   scripts/respaldo.ps1 restaurar <archivo>    carga ese respaldo (¡reemplaza los datos actuales!)
# El dump usa --single-transaction: es consistente sin bloquear la operación. Incluye procedimientos, funciones y vistas.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$dc = @('compose', '--env-file', '.env', '-f', 'infra/docker-compose.yml')
$accion = if ($args.Count -gt 0) { $args[0] } else { 'respaldar' }

switch ($accion) {
    'respaldar' {
        New-Item -ItemType Directory -Force respaldos | Out-Null
        $nombre = "caja_comun-$(Get-Date -Format 'yyyyMMdd-HHmmss').sql.gz"
        # Se comprime dentro del contenedor y se copia el archivo: redirigir binarios con PowerShell 5.1 los corrompe.
        docker @dc exec -T mariadb sh -c 'mariadb-dump -uroot -p"$MARIADB_ROOT_PASSWORD" --single-transaction --routines --events --triggers "$MARIADB_DATABASE" | gzip > /tmp/respaldo.sql.gz'
        if ($LASTEXITCODE -ne 0) { throw 'El respaldo falló.' }
        docker @dc cp mariadb:/tmp/respaldo.sql.gz "respaldos/$nombre"
        docker @dc exec -T mariadb rm -f /tmp/respaldo.sql.gz
        Write-Host "Respaldo guardado en respaldos/$nombre ($([math]::Round((Get-Item "respaldos/$nombre").Length / 1KB)) KB)"
    }
    'restaurar' {
        if ($args.Count -lt 2 -or -not (Test-Path $args[1])) { throw 'Uso: scripts/respaldo.ps1 restaurar <archivo.sql.gz>' }
        Write-Host "Se reemplazarán los datos actuales con $($args[1])."
        docker @dc cp $args[1] mariadb:/tmp/restaurar.sql.gz
        docker @dc exec -T mariadb sh -c 'gunzip -c /tmp/restaurar.sql.gz | mariadb -uroot -p"$MARIADB_ROOT_PASSWORD" "$MARIADB_DATABASE" && rm -f /tmp/restaurar.sql.gz'
        if ($LASTEXITCODE -ne 0) { throw 'La restauración falló.' }
        Write-Host 'Restaurado. Reinicia la API para limpiar la caché: docker compose restart api'
    }
    default { Write-Host 'Uso: scripts/respaldo.ps1 [restaurar <archivo>]'; exit 2 }
}

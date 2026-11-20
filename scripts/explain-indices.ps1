# Corre db/explain_indices.sql contra la base del compose (conviene tener el perfil full sembrado).
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
Get-Content db/explain_indices.sql -Raw -Encoding UTF8 |
    docker compose --env-file .env -f infra/docker-compose.yml exec -T mariadb sh -c 'mariadb --table -u"$MARIADB_USER" -p"$MARIADB_PASSWORD" "$MARIADB_DATABASE"'

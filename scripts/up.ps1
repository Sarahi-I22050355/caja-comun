# Levanta la pila completa. Si no existe .env lo crea desde .env.example con contraseñas aleatorias.
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

function Nueva-Clave([int]$largo = 24) { -join ((48..57) + (65..90) + (97..122) | Get-Random -Count $largo | ForEach-Object { [char]$_ }) }

if (-not (Test-Path .env)) {
    $lineas = Get-Content .env.example | ForEach-Object {
        if ($_ -match '^(MARIADB_ROOT_PASSWORD|MARIADB_PASSWORD)=$') { "$($matches[1])=$(Nueva-Clave)" }
        elseif ($_ -match '^JWT_SECRET=$') { "JWT_SECRET=$(Nueva-Clave 48)" }
        else { $_ }
    }
    # UTF-8 sin BOM para que docker compose lea bien el archivo
    [IO.File]::WriteAllLines((Join-Path (Get-Location) '.env'), $lineas)
    Write-Host 'Se creó .env con contraseñas aleatorias.'
}
# Un .env anterior puede no traer JWT_SECRET: se agrega sin tocar el resto.
$lineas = [System.Collections.Generic.List[string]](Get-Content .env)
$i = $lineas.FindIndex({ param($l) $l -match '^JWT_SECRET=' })
if ($i -lt 0) { $lineas.Add("JWT_SECRET=$(Nueva-Clave 48)"); $cambio = $true }
elseif ($lineas[$i] -match '^JWT_SECRET=\s*$') { $lineas[$i] = "JWT_SECRET=$(Nueva-Clave 48)"; $cambio = $true }
if ($cambio) {
    [IO.File]::WriteAllLines((Join-Path (Get-Location) '.env'), $lineas)
    Write-Host 'Se generó JWT_SECRET en .env.'
}
docker compose --env-file .env -f infra/docker-compose.yml up -d --build @args

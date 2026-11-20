# Parte 2: federación de consultas (Trino + MariaDB + PostgreSQL + ClickHouse). Es opcional: con el perfil demo usa unos 1.2 GB de memoria (límite de 2.5 GB entre los tres).
#   scripts/federacion.ps1 iniciar              levanta los motores, crea el usuario de solo lectura de MariaDB y carga los datos
#   scripts/federacion.ps1 cargar               vuelve a copiar las cajas desde MariaDB (hazlo después de cambiar datos)
#   scripts/federacion.ps1 verificar [--anio N] corre las cinco consultas federadas, las compara con MariaDB y escribe
#                                               docs/consultas-parte2-evidencia.md
#   scripts/federacion.ps1 detener              apaga los tres contenedores (los datos quedan en sus volúmenes)
$ErrorActionPreference = 'Stop'
# La salida de docker es UTF-8: sin esto PowerShell 5.1 la decodifica con la página de códigos local y rompe los acentos.
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
Set-Location (Join-Path $PSScriptRoot '..')
$accion = if ($args.Count -gt 0) { $args[0] } else { 'iniciar' }
$resto = if ($args.Count -gt 1) { $args[1..($args.Count - 1)] } else { @() }
$dc = @('compose', '--env-file', '.env', '-f', 'infra/docker-compose.yml')
$dcq = @('compose', '--progress', 'quiet', '--env-file', '.env', '-f', 'infra/docker-compose.yml')   # sin el detalle del build

function Nueva-Clave([int]$largo = 24) { -join ((48..57) + (65..90) + (97..122) | Get-Random -Count $largo | ForEach-Object { [char]$_ }) }

function Valor-Env([string]$nombre) {
    $l = Get-Content .env | Where-Object { $_ -match "^$nombre=(.*)$" } | Select-Object -First 1
    if ($l -match "^$nombre=(.*)$") { return $matches[1].Trim() }
    return ''
}

# Las contraseñas de la federación se generan una vez y se guardan en .env (que no se versiona).
function Asegurar-Claves {
    if (-not (Test-Path .env)) { throw 'No existe .env: corre primero scripts/up.ps1.' }
    $lineas = [System.Collections.Generic.List[string]](Get-Content .env)
    $cambio = $false
    foreach ($n in 'FEDERACION_MARIADB_PASSWORD', 'FEDERACION_POSTGRES_PASSWORD', 'FEDERACION_CLICKHOUSE_PASSWORD') {
        $i = $lineas.FindIndex({ param($l) $l -match "^$n=" })
        if ($i -lt 0) { $lineas.Add("$n=$(Nueva-Clave)"); $cambio = $true }
        elseif ($lineas[$i] -match "^$n=\s*$") { $lineas[$i] = "$n=$(Nueva-Clave)"; $cambio = $true }
    }
    if (-not ($lineas | Where-Object { $_ -match '^TRINO_PORT=' })) { $lineas.Add('TRINO_PORT=8081'); $cambio = $true }
    if ($cambio) {
        # UTF-8 sin BOM para que docker compose lea bien el archivo
        [IO.File]::WriteAllLines((Join-Path (Get-Location) '.env'), $lineas)
        Write-Host 'Se generaron las contraseñas de la federación en .env.'
    }
}

function Esperar-Sano([string]$servicio, [int]$segundos = 300) {
    $limite = (Get-Date).AddSeconds($segundos)
    while ((Get-Date) -lt $limite) {
        $id = (docker @dc --profile federation ps -q $servicio)
        if ($id) {
            $estado = docker inspect --format '{{.State.Health.Status}}' $id
            if ($estado -eq 'healthy') { return }
        }
        Start-Sleep -Seconds 3
    }
    throw "$servicio no quedó listo en $segundos s. Revisa: docker compose --profile federation logs $servicio"
}

function Cargar {
    docker @dcq run --rm --build api cargar-federacion
    if ($LASTEXITCODE -ne 0) { throw 'La carga falló.' }
}

switch ($accion) {
    'iniciar' {
        Asegurar-Claves
        docker @dc up -d --build
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo levantar la pila base.' }
        # Usuario de solo lectura para Trino: la federación nunca escribe en MariaDB.
        $clave = Valor-Env 'FEDERACION_MARIADB_PASSWORD'
        $sql = "CREATE USER IF NOT EXISTS 'federacion'@'%' IDENTIFIED BY '$clave'; ALTER USER 'federacion'@'%' IDENTIFIED BY '$clave'; GRANT SELECT ON caja_comun.* TO 'federacion'@'%'; FLUSH PRIVILEGES;"
        $sql | docker @dc exec -T mariadb sh -c 'mariadb -uroot -p"$MARIADB_ROOT_PASSWORD"'
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo crear el usuario de solo lectura de MariaDB.' }
        docker @dc --profile federation up -d
        if ($LASTEXITCODE -ne 0) { throw 'No se pudo levantar la federación.' }
        Esperar-Sano 'trino'
        Cargar
        Write-Host 'Federación lista. Verifica con: scripts/federacion.ps1 verificar'
    }
    'cargar' { Cargar }
    'verificar' {
        $salida = docker @dcq run --rm --build api verificar-federacion @resto
        $codigo = $LASTEXITCODE
        $salida | ForEach-Object { Write-Host $_ }
        if ($codigo -eq 0 -or $codigo -eq 3) {
            $encabezado = @(
                '# Parte 2: evidencia de equivalencia',
                '',
                "Resultado de scripts/federacion.ps1 verificar el $(Get-Date -Format 'yyyy-MM-dd HH:mm') (hora local).",
                'Cada consulta federada (docs/consultas-parte2.sql, Trino sobre MariaDB + PostgreSQL + ClickHouse) se compara fila por fila',
                'con la misma consulta directa sobre MariaDB (docs/consultas-parte2-mariadb.sql). "idéntico" significa que ambos',
                'conjuntos de filas son exactamente iguales, con las mismas columnas. Los tiempos son los medidos en esta corrida.',
                ''
            )
            [IO.File]::WriteAllLines((Join-Path (Get-Location) 'docs/consultas-parte2-evidencia.md'), ($encabezado + ($salida | Where-Object { $_ -is [string] -and ($_ -match '^\|' -or $_ -eq '') })))
            Write-Host 'Evidencia escrita en docs/consultas-parte2-evidencia.md'
        }
        exit $codigo
    }
    'detener' { docker @dc --profile federation stop trino clickhouse postgres }
    default { Write-Host 'Uso: scripts/federacion.ps1 iniciar | cargar | verificar [--anio N] | detener'; exit 2 }
}

# Mide la latencia de los endpoints principales de la API (p50, p95 y máximo) y marca los que pasan de 500 ms.
#   scripts/medir-api.ps1 -Correo admin@cajacomun.mx -Password '<la del admin>' [-Repeticiones 15] [-Url http://localhost:8080]
# Con la contraseña vacía toma la variable SEED_PASSWORD_ADMIN. La primera llamada de cada ruta (en frío, sin caché) se
# reporta aparte de las demás (con caché en Redis).
param(
    [string]$Correo = 'admin@cajacomun.mx',
    [string]$Password = $env:SEED_PASSWORD_ADMIN,
    [int]$Repeticiones = 15,
    [string]$Url = 'http://localhost:8080'
)
$ErrorActionPreference = 'Stop'
if (-not $Password) { throw 'Falta la contraseña: usa -Password o la variable SEED_PASSWORD_ADMIN.' }

$sesion = Invoke-RestMethod -Method Post "$Url/api/v1/auth/login" -ContentType 'application/json' `
    -Body (@{ correo = $Correo; password = $Password } | ConvertTo-Json)
$h = @{ Authorization = "Bearer $($sesion.accessToken)" }
$cajas = Invoke-RestMethod "$Url/api/v1/cajas" -Headers $h
$caja = $cajas[0].id
$periodos = Invoke-RestMethod "$Url/api/v1/cajas/$caja/periodos?anio=2024" -Headers $h
$periodo = ($periodos | Where-Object { $_.estado -eq 'APLICADO' } | Select-Object -Last 1).id
if (-not $periodo) { $periodo = $periodos[0].id }
$ahorrador = (Invoke-RestMethod "$Url/api/v1/ahorradores?cajaId=$caja&pageSize=1" -Headers $h).items[0].id

$rutas = [ordered]@{
    'GET /dashboard'                 = "/api/v1/dashboard?cajaId=$caja"
    'GET /ahorradores (página)'      = "/api/v1/ahorradores?cajaId=$caja&page=1&pageSize=25"
    'GET /ahorradores?q= (búsqueda)' = "/api/v1/ahorradores?cajaId=$caja&q=gar"
    'GET /ahorradores/{id}'          = "/api/v1/ahorradores/$ahorrador"
    'GET /ahorradores/{id}/movimientos' = "/api/v1/ahorradores/$ahorrador/movimientos?pageSize=25"
    'GET /nomina/descuentos'         = "/api/v1/nomina/periodos/$periodo/descuentos"
    'GET /prestamos (página)'        = "/api/v1/prestamos?cajaId=$caja&pageSize=25"
    'GET /prestamos/resumen'         = "/api/v1/prestamos/resumen?cajaId=$caja"
    'GET /ahorro-voluntario'         = "/api/v1/ahorro-voluntario?cajaId=$caja&pageSize=25"
    'GET /reportes/estado-resultados' = "/api/v1/reportes/estado-resultados?cajaId=$caja&anio=2023"
    'GET /ciclos'                    = "/api/v1/ciclos?cajaId=$caja"
}

function Percentil([double[]]$v, [double]$p) {
    $o = $v | Sort-Object
    $o[[math]::Min($o.Count - 1, [math]::Ceiling($p * $o.Count) - 1)]
}

"{0,-36} {1,9} {2,9} {3,9} {4,9}" -f 'Endpoint', 'frío ms', 'p50 ms', 'p95 ms', 'máx ms'
foreach ($nombre in $rutas.Keys) {
    $tiempos = @()
    for ($i = 0; $i -lt $Repeticiones; $i++) {
        $t = Measure-Command { Invoke-RestMethod ($Url + $rutas[$nombre]) -Headers $h | Out-Null }
        $tiempos += $t.TotalMilliseconds
    }
    $frio = $tiempos[0]
    $resto = $tiempos[1..($tiempos.Count - 1)]
    $marca = if ((Percentil $resto 0.95) -gt 500 -or $frio -gt 500) { '  <-- > 500 ms' } else { '' }
    "{0,-36} {1,9:N0} {2,9:N0} {3,9:N0} {4,9:N0}{5}" -f $nombre, $frio, (Percentil $resto 0.5), (Percentil $resto 0.95), ($resto | Measure-Object -Maximum).Maximum, $marca
}

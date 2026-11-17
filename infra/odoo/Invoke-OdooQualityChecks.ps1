[CmdletBinding()]
param(
    [string] $EnvironmentFile = (Join-Path (Split-Path $PSScriptRoot -Parent | Split-Path -Parent) ".env"),
    [string] $EvidenceFile = (Join-Path $PSScriptRoot "evidence\infrastructure-sale-latest.json")
)

$ErrorActionPreference = "Stop"
$composeFile = Join-Path $PSScriptRoot "docker-compose.yml"
if (-not (Test-Path -LiteralPath $EnvironmentFile -PathType Leaf)) {
    throw "No existe $EnvironmentFile. Copia .env.example a .env y reemplaza sus valores locales."
}

function Invoke-Compose {
    & docker compose --env-file $EnvironmentFile -f $composeFile @args
    if ($LASTEXITCODE -ne 0) { throw "docker compose fallo con codigo $LASTEXITCODE" }
}

$databaseLine = Get-Content -LiteralPath $EnvironmentFile |
    Where-Object { $_ -match '^\s*Odoo__Database\s*=' } |
    Select-Object -Last 1
if (-not $databaseLine) { throw "Falta Odoo__Database en el archivo de entorno." }
$database = ($databaseLine -split '=', 2)[1].Trim().Trim('"').Trim("'")
if ($database -notmatch '^[A-Za-z0-9_-]+$') { throw "Odoo__Database contiene caracteres no permitidos." }
Invoke-Compose config --quiet
Invoke-Compose up -d postgres
Invoke-Compose run --rm odoo odoo -d $database -i point_of_sale --without-demo=True --stop-after-init
Invoke-Compose up -d odoo

$deadline = [DateTime]::UtcNow.AddMinutes(5)
do {
    $health = (& docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' proyectopatrones-odoo 2>$null)
    if ($health -eq "healthy") { break }
    if ([DateTime]::UtcNow -ge $deadline) { throw "Odoo no alcanzo el estado healthy dentro de cinco minutos." }
    Start-Sleep -Seconds 5
} while ($true)

Get-Content -Raw (Join-Path $PSScriptRoot "scripts\bootstrap_pos.py") |
    & docker compose --env-file $EnvironmentFile -f $composeFile exec -T -e "ODOO_DATABASE=$database" odoo sh -c 'odoo shell -d "$ODOO_DATABASE" --db_host "$HOST" --db_user "$USER" --db_password "$PASSWORD"'
if ($LASTEXITCODE -ne 0) { throw "No se pudo preparar la configuracion POS." }

$infrastructureOutput = Get-Content -Raw (Join-Path $PSScriptRoot "scripts\infrastructure_sale.py") |
    & docker compose --env-file $EnvironmentFile -f $composeFile exec -T -e "ODOO_DATABASE=$database" odoo sh -c 'odoo shell -d "$ODOO_DATABASE" --db_host "$HOST" --db_user "$USER" --db_password "$PASSWORD"'
if ($LASTEXITCODE -ne 0) { throw "La prueba de infraestructura Odoo fallo." }

$evidenceLine = $infrastructureOutput | Where-Object { $_ -like 'INFRASTRUCTURE_EVIDENCE *' } | Select-Object -Last 1
if (-not $evidenceLine) { throw "La prueba de infraestructura no produjo evidencia verificable." }
$json = $evidenceLine.Substring('INFRASTRUCTURE_EVIDENCE '.Length)
$parsed = $json | ConvertFrom-Json
if ($parsed.result -ne "PASS" -or $parsed.line_count -ne 2 -or $parsed.payment_kind -ne "cash") {
    throw "La evidencia de infraestructura no cumple el contrato esperado."
}

$evidenceDirectory = Split-Path -Parent $EvidenceFile
New-Item -ItemType Directory -Force -Path $evidenceDirectory | Out-Null
$parsed | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $EvidenceFile -Encoding utf8
Write-Host "Prueba de infraestructura PASS. Evidencia guardada en $EvidenceFile"

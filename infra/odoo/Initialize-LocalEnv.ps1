$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$target = Join-Path $root '.env'
if (Test-Path -LiteralPath $target) {
    Write-Output '.env ya existe; no se modificó.'
    exit 0
}

function New-Secret([int]$bytes = 32) {
    $buffer = [byte[]]::new($bytes)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($buffer)
    [Convert]::ToHexString($buffer).ToLowerInvariant()
}

$content = @(
    'Odoo__BaseUrl=http://localhost:8069'
    'Odoo__Database=pos_demo'
    'Odoo__ApiKey=configure-after-creating-odoo-integration-user'
    'Odoo__TimeoutSeconds=15'
    "ODOO_POSTGRES_PASSWORD=$(New-Secret 24)"
    'ODOO_PORT=8069'
    "JWT_SECRET=$(New-Secret 48)"
    'API_PORT=5080'
    'Pos__Mode=Odoo'
)
[System.IO.File]::WriteAllLines($target, $content, [System.Text.UTF8Encoding]::new($false))
Write-Output '.env local creado con secretos aleatorios. La API key de Odoo queda pendiente de configuración.'

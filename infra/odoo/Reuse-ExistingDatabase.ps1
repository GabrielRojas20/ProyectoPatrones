$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$envFile = Join-Path $root '.env'
if (-not (Test-Path -LiteralPath $envFile)) { throw 'Ejecuta Initialize-LocalEnv.ps1 primero.' }

$lines = [System.IO.File]::ReadAllLines($envFile)
$passwordLine = $lines | Where-Object { $_.StartsWith('ODOO_POSTGRES_PASSWORD=') } | Select-Object -First 1
$password = $passwordLine.Substring('ODOO_POSTGRES_PASSWORD='.Length)
if ($password -notmatch '^[a-f0-9]{32,128}$') { throw 'La contraseña local no tiene el formato seguro esperado.' }

$sql = "ALTER ROLE odoo WITH PASSWORD '$password';"
$sql | docker exec -i proyectopatrones-postgres psql -v ON_ERROR_STOP=1 -U odoo -d postgres | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'No se pudo sincronizar la contraseña de PostgreSQL.' }

$updated = $lines | ForEach-Object {
    if ($_.StartsWith('Odoo__Database=')) { 'Odoo__Database=pos_demo_codex' } else { $_ }
}
[System.IO.File]::WriteAllLines($envFile, $updated, [System.Text.UTF8Encoding]::new($false))
Write-Output 'Base existente seleccionada y contraseña PostgreSQL sincronizada.'

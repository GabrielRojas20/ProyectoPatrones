$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$envFile = Join-Path $root '.env'
if (-not (Test-Path -LiteralPath $envFile)) { throw 'Falta .env. Ejecuta infra/odoo/Initialize-LocalEnv.ps1.' }

Get-Content -LiteralPath $envFile | ForEach-Object {
    if ($_ -match '^(?<name>[A-Za-z_][A-Za-z0-9_]*)=(?<value>.*)$') {
        [Environment]::SetEnvironmentVariable($matches.name, $matches.value, 'Process')
    }
}

dotnet run --project (Join-Path $root 'backend\src\ProyectoPatrones.Api\ProyectoPatrones.Api.csproj') --configuration Release --no-build

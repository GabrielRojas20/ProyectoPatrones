$ErrorActionPreference = 'Stop'
$root = Resolve-Path $PSScriptRoot
$envFile = Join-Path $root '.env'

if (-not (Test-Path -LiteralPath $envFile)) {
    & (Join-Path $root 'infra\odoo\Initialize-LocalEnv.ps1')
}

Write-Host '1/4 Iniciando Odoo y PostgreSQL...' -ForegroundColor Cyan
docker compose --env-file $envFile -f (Join-Path $root 'infra\odoo\docker-compose.yml') up -d
if ($LASTEXITCODE -ne 0) { throw 'Docker no pudo iniciar Odoo.' }

$listener = Get-NetTCPConnection -LocalPort 5080 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($listener) { Stop-Process -Id $listener.OwningProcess -Force }

Write-Host '2/4 Compilando la aplicación...' -ForegroundColor Cyan
dotnet build (Join-Path $root 'backend\src\ProyectoPatrones.Api\ProyectoPatrones.Api.csproj') --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'La compilación falló.' }

Write-Host '3/4 Iniciando backend y frontend...' -ForegroundColor Cyan
Start-Process powershell -ArgumentList '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $root 'scripts\Run-Local.ps1') -WorkingDirectory $root -WindowStyle Hidden

$ready = $false
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    Start-Sleep -Milliseconds 500
    try {
        $null = Invoke-RestMethod -Uri 'http://localhost:5080/api/status'
        $ready = $true
        break
    } catch {}
}
if (-not $ready) { throw 'La aplicación no respondió en http://localhost:5080.' }

Write-Host 'Abriendo POS y Odoo en el navegador...' -ForegroundColor Cyan
Start-Process 'http://localhost:5080/'
Start-Process 'http://localhost:8069/web'

Write-Host '4/4 Iniciando servidor Node.js para la red local...' -ForegroundColor Cyan
$oldNode = Get-NetTCPConnection -LocalPort 5173 -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($oldNode) { Stop-Process -Id $oldNode.OwningProcess -Force }
$lanLog = Join-Path $env:TEMP 'proyecto-patrones-lan.log'
$node = Start-Process node -ArgumentList (Join-Path $root 'scripts\lan-server.mjs') -RedirectStandardOutput $lanLog -PassThru -WindowStyle Hidden
for ($attempt = 0; $attempt -lt 20; $attempt++) {
    Start-Sleep -Milliseconds 250
    $line = Get-Content $lanLog -ErrorAction SilentlyContinue | Select-Object -Last 1
    if ($line -match '^LAN_URL=(.+)$') { $lanUrl = $matches[1]; break }
}
if (-not $lanUrl) { throw 'Node.js no pudo iniciar el servidor LAN.' }
Write-Host "ENLACE PARA TU AMIGO: $lanUrl" -ForegroundColor Green
Write-Host 'Ambos deben estar conectados a la misma red. No cierres esta ventana.' -ForegroundColor Yellow
Start-Process $lanUrl
Wait-Process -Id $node.Id

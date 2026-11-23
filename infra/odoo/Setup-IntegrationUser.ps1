$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$envFile = Join-Path $root '.env'
if (-not (Test-Path -LiteralPath $envFile)) { throw 'Falta .env.' }

$python = @'
import datetime
import secrets
group = env.ref('point_of_sale.group_pos_user')
users = env['res.users'].sudo()
user = users.search([('login', '=', 'pos.integration')], limit=1)
if not user:
    user = users.create({
        'name': 'Integración POS local',
        'login': 'pos.integration',
        'email': 'pos.integration@localhost',
        'password': secrets.token_urlsafe(32),
        'group_ids': [(6, 0, [group.id])],
    })
keys = env['res.users.apikeys'].sudo()
keys.search([('user_id', '=', user.id), ('name', '=', 'ProyectoPatrones local')]).unlink()
expiration = datetime.datetime.now(datetime.UTC).replace(tzinfo=None) + datetime.timedelta(days=89)
key = env['res.users.apikeys'].with_user(user)._generate('rpc', 'ProyectoPatrones local', expiration)
env.cr.commit()
print('CODEX_API_KEY=' + key)
'@

$output = $python | docker exec -i proyectopatrones-odoo bash -lc 'odoo shell -d pos_demo_codex --db_host="$HOST" --db_user="$USER" --db_password="$PASSWORD" --no-http'
if ($LASTEXITCODE -ne 0) { throw 'Odoo no pudo crear el usuario de integración.' }
$marker = $output | Where-Object { $_ -like 'CODEX_API_KEY=*' } | Select-Object -Last 1
if (-not $marker) { throw 'Odoo no devolvió la API key esperada.' }
$key = $marker.Substring('CODEX_API_KEY='.Length)
if ($key -notmatch '^[a-f0-9]{40}_oak$') { throw 'La API key generada no tiene el formato esperado.' }

$lines = [System.IO.File]::ReadAllLines($envFile) | ForEach-Object {
    if ($_.StartsWith('Odoo__ApiKey=')) { "Odoo__ApiKey=$key" } else { $_ }
}
[System.IO.File]::WriteAllLines($envFile, $lines, [System.Text.UTF8Encoding]::new($false))
Write-Output 'Usuario POS de integración y API key configurados localmente.'

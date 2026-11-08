param(
    [string] $EnvironmentFile = (Join-Path (Split-Path $PSScriptRoot -Parent | Split-Path -Parent) ".env")
)

$ErrorActionPreference = "Stop"
if (-not (Test-Path -LiteralPath $EnvironmentFile -PathType Leaf)) {
    throw "No existe el archivo local de configuración: $EnvironmentFile. Copia .env.example a .env y completa sus valores."
}

foreach ($line in Get-Content -LiteralPath $EnvironmentFile) {
    $trimmed = $line.Trim()
    if ($trimmed.Length -eq 0 -or $trimmed.StartsWith("#")) { continue }

    $separator = $trimmed.IndexOf("=")
    if ($separator -lt 1) { continue }

    $name = $trimmed.Substring(0, $separator).Trim()
    $value = $trimmed.Substring($separator + 1).Trim()
    if ($name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
        throw "Nombre de variable inválido en el archivo .env: $name"
    }

    if ($value.Length -ge 2 -and $value[0] -eq '"' -and $value[$value.Length - 1] -eq '"') {
        $value = $value.Substring(1, $value.Length - 2)
    }
    elseif ($value.Length -ge 2 -and $value[0] -eq "'" -and $value[$value.Length - 1] -eq "'") {
        $value = $value.Substring(1, $value.Length - 2)
    }

    Set-Item -Path "Env:$name" -Value $value
}

Write-Host "Configuración local cargada en este proceso. No se muestran los valores secretos."

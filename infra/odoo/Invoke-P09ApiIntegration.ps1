[CmdletBinding()]
param(
    [string] $ApiBaseUrl = 'http://localhost:8080',
    [string] $OperatorUsername = $(if ($env:Auth__OperatorUsername) { $env:Auth__OperatorUsername } else { 'operator' }),
    [string] $EvidenceFile = (Join-Path $PSScriptRoot 'evidence\p09-api-latest.json')
)

$ErrorActionPreference = 'Stop'
$ApiBaseUrl = $ApiBaseUrl.TrimEnd('/')
$parsedApiBaseUrl = $null
if (-not [Uri]::TryCreate($ApiBaseUrl, [UriKind]::Absolute, [ref]$parsedApiBaseUrl) -or
    $parsedApiBaseUrl.Scheme -notin @('http', 'https') -or $parsedApiBaseUrl.UserInfo -or
    $parsedApiBaseUrl.Query -or $parsedApiBaseUrl.Fragment -or
    $parsedApiBaseUrl.Host -in @('0.0.0.0', '::', '[::]')) {
    throw 'ApiBaseUrl debe ser una URL HTTP(S) absoluta, sin credenciales, parámetros ni direcciones comodín como 0.0.0.0.'
}
$script:EvidenceDirectory = Split-Path -Parent $EvidenceFile
$script:IdempotencyKey = [Guid]::NewGuid().ToString('N')
$script:PostStatusCode = 0
$script:PostTraceId = $null

function Save-P09Evidence {
    param([Parameter(Mandatory)][object] $Evidence)

    New-Item -ItemType Directory -Force -Path $script:EvidenceDirectory | Out-Null
    $Evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $EvidenceFile -Encoding UTF8
}

function Invoke-ApiJson {
    param(
        [Parameter(Mandatory)][ValidateSet('GET', 'POST')][string] $Method,
        [Parameter(Mandatory)][string] $Path,
        [hashtable] $Headers = @{},
        [object] $Body
    )

    $request = @{
        Uri             = "$ApiBaseUrl$Path"
        Method          = $Method
        Headers         = $Headers
        UseBasicParsing = $true
        TimeoutSec      = 60
        ErrorAction     = 'Stop'
    }
    if ($null -ne $Body) {
        $request.ContentType = 'application/json; charset=utf-8'
        $request.Body = $Body | ConvertTo-Json -Depth 8 -Compress
    }

    try {
        $response = Invoke-WebRequest @request
        $parsedBody = $null
        if (-not [string]::IsNullOrWhiteSpace($response.Content)) {
            $parsedBody = $response.Content | ConvertFrom-Json
        }
        return [pscustomobject]@{ StatusCode = [int]$response.StatusCode; Body = $parsedBody }
    }
    catch {
        $httpResponse = $_.Exception.Response
        if ($null -eq $httpResponse) {
            return [pscustomobject]@{ StatusCode = 0; Body = $null }
        }

        $statusCode = [int]$httpResponse.StatusCode
        $responseBody = $null
        try {
            $stream = $httpResponse.GetResponseStream()
            if ($null -ne $stream) {
                $reader = [System.IO.StreamReader]::new($stream)
                $text = $reader.ReadToEnd()
                $reader.Dispose()
                if (-not [string]::IsNullOrWhiteSpace($text)) {
                    $responseBody = $text | ConvertFrom-Json
                }
            }
        }
        catch { $responseBody = $null }
        return [pscustomobject]@{ StatusCode = $statusCode; Body = $responseBody }
    }
}

function Get-ApiErrorFields {
    param([object] $Body)
    $code = $null
    $traceId = $null
    if ($null -ne $Body) {
        $code = $Body.code
        $traceId = $Body.traceId
    }
    return [pscustomobject]@{ Code = $code; TraceId = $traceId }
}

function Assert-HttpSuccess {
    param([Parameter(Mandatory)][object] $Response, [Parameter(Mandatory)][string] $Operation)
    if ($Response.StatusCode -lt 200 -or $Response.StatusCode -ge 300) {
        $errorFields = Get-ApiErrorFields $Response.Body
        $trace = if ($errorFields.TraceId) { "; traceId=$($errorFields.TraceId)" } else { '' }
        $code = if ($errorFields.Code) { "; code=$($errorFields.Code)" } else { '' }
        throw "$Operation falló con HTTP $($Response.StatusCode)$code$trace."
    }
}

function Get-UniqueAvailableProduct {
    param([Parameter(Mandatory)][string] $Code, [Parameter(Mandatory)][hashtable] $Headers)

    $encodedCode = [Uri]::EscapeDataString($Code)
    $response = Invoke-ApiJson -Method GET -Path "/api/products?code=$encodedCode&page=1&pageSize=10" -Headers $Headers
    Assert-HttpSuccess $response "Consulta de producto $Code"
    $matches = @($response.Body.items | Where-Object { $_.code -eq $Code -and $_.available -eq $true })
    if ($matches.Count -ne 1) { throw "Se esperaba un producto único y disponible para $Code; se encontraron $($matches.Count)." }
    return $matches[0]
}

function ConvertTo-ConfirmedStatus {
    param([object] $Value)
    return ([string]$Value -eq 'Confirmed' -or [string]$Value -eq '1')
}

$passwordSecure = Read-Host "Contraseña de $OperatorUsername (no se guarda)" -AsSecureString
$passwordPointer = [IntPtr]::Zero
$password = $null
try {
    $passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($passwordSecure)
    $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)

    $loginBody = @{ username = $OperatorUsername; password = $password }
    $login = Invoke-ApiJson -Method POST -Path '/api/auth/login' -Body $loginBody
    Assert-HttpSuccess $login 'Inicio de sesión'
    if ([string]::IsNullOrWhiteSpace($login.Body.accessToken)) { throw 'La API no devolvió un token de acceso.' }
    $headers = @{ Authorization = "Bearer $($login.Body.accessToken)" }
}
finally {
    if ($null -ne $loginBody) { $loginBody.password = $null }
    $loginBody = $null
    $password = $null
    if ($passwordPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    }
    if ($null -ne $passwordSecure) { $passwordSecure.Dispose() }
}

$productA = Get-UniqueAvailableProduct -Code 'POS-CAFE' -Headers $headers
$productB = Get-UniqueAvailableProduct -Code 'POS-SANDWICH' -Headers $headers
$sessionResponse = Invoke-ApiJson -Method GET -Path '/api/pos/sessions/active' -Headers $headers
Assert-HttpSuccess $sessionResponse 'Consulta de sesión POS'
$session = $sessionResponse.Body
if ($session.isOpen -ne $true) { throw 'La API no devolvió una sesión POS abierta.' }

$cashMethods = @($session.paymentMethods | Where-Object { $_.name -eq 'Efectivo Proyecto' })
if ($cashMethods.Count -ne 1) { throw 'La sesión no tiene un método único llamado Efectivo Proyecto.' }
$cashMethod = $cashMethods[0]
$requestBody = @{
    operationDate   = (Get-Date -Format 'yyyy-MM-dd')
    sessionId       = [long]$session.id
    paymentMethodId = [long]$cashMethod.id
    lines           = @(
        @{ productId = [long]$productA.id; quantity = 1; expectedUnitPrice = [decimal]$productA.price; discountPercent = 0 }
        @{ productId = [long]$productB.id; quantity = 1; expectedUnitPrice = [decimal]$productB.price; discountPercent = 0 }
    )
    customer        = $null
}

$saleResponse = Invoke-ApiJson -Method POST -Path '/api/sales' -Headers (@{ Authorization = $headers.Authorization; 'Idempotency-Key' = $script:IdempotencyKey }) -Body $requestBody
$script:PostStatusCode = $saleResponse.StatusCode
$postError = Get-ApiErrorFields $saleResponse.Body
$script:PostTraceId = $postError.TraceId
if ($saleResponse.StatusCode -lt 200 -or $saleResponse.StatusCode -ge 300) {
    $uncertain = ($saleResponse.StatusCode -eq 0 -or $saleResponse.StatusCode -ge 500 -or $postError.Code -eq 'odoo.result_uncertain')
    $result = if ($uncertain) { 'UNCERTAIN' } else { 'FAIL' }
    Save-P09Evidence ([ordered]@{
        schema_version = 1
        result = $result
        executed_at_utc = [DateTime]::UtcNow.ToString('o')
        api_base_url = $ApiBaseUrl
        request = [ordered]@{ method = 'POST'; path = '/api/sales'; http_status = $saleResponse.StatusCode; idempotency_key = $script:IdempotencyKey }
        error = [ordered]@{ code = $postError.Code; trace_id = $postError.TraceId }
        note = 'No se guarda el cuerpo de la solicitud, credenciales, token ni encabezados de autenticación. Si el resultado es incierto, concilie esta clave antes de reintentar.'
    })
    throw "La API no confirmó la venta (HTTP $($saleResponse.StatusCode); resultado $result). Evidencia: $EvidenceFile"
}

$sale = $saleResponse.Body
if ($sale.status -ne 'confirmed' -or [string]::IsNullOrWhiteSpace([string]$sale.externalId)) {
    Save-P09Evidence ([ordered]@{
        schema_version = 1; result = 'UNCERTAIN'; executed_at_utc = [DateTime]::UtcNow.ToString('o')
        api_base_url = $ApiBaseUrl
        request = [ordered]@{ method = 'POST'; path = '/api/sales'; http_status = $saleResponse.StatusCode; idempotency_key = $script:IdempotencyKey }
        response = [ordered]@{ status = $sale.status; sale_id = $sale.id; external_id = $sale.externalId }
        note = 'La respuesta no confirma el ID externo de Odoo. No reintente con otra clave hasta conciliar la operación.'
    })
    throw 'La API no devolvió una venta confirmada con ID externo de Odoo; P09 sigue pendiente.'
}

$detailPath = '/api/sales/' + [Uri]::EscapeDataString([string]$sale.externalId)
$detailResponse = Invoke-ApiJson -Method GET -Path $detailPath -Headers $headers
if ($detailResponse.StatusCode -lt 200 -or $detailResponse.StatusCode -ge 300) {
    $detailError = Get-ApiErrorFields $detailResponse.Body
    Save-P09Evidence ([ordered]@{
        schema_version = 1
        result = 'UNCERTAIN'
        executed_at_utc = [DateTime]::UtcNow.ToString('o')
        api_base_url = $ApiBaseUrl
        request = [ordered]@{ method = 'POST'; path = '/api/sales'; http_status = $saleResponse.StatusCode; idempotency_key = $script:IdempotencyKey }
        response = [ordered]@{ status = $sale.status; sale_id = $sale.id; external_id = $sale.externalId }
        verification = [ordered]@{ method = 'GET'; path = $detailPath; http_status = $detailResponse.StatusCode; error_code = $detailError.Code; trace_id = $detailError.TraceId }
        note = 'La creación fue confirmada, pero no se pudo verificar el detalle. Concilie la orden en Odoo antes de reintentar.'
    })
    throw "La orden fue confirmada, pero la consulta de verificación falló (HTTP $($detailResponse.StatusCode)). Evidencia: $EvidenceFile"
}
$detail = $detailResponse.Body
$detailLines = @($detail.lines)
$confirmed = ConvertTo-ConfirmedStatus $detail.status
$lineProducts = @($detailLines | ForEach-Object { [string]$_.productId } | Sort-Object)
$expectedProducts = @([string]$productA.id, [string]$productB.id) | Sort-Object
if (-not $confirmed -or $detailLines.Count -ne 2 -or [long]$detail.paymentMethodId -ne [long]$cashMethod.id -or
    [string]$detail.externalId -ne [string]$sale.externalId -or ($lineProducts -join ',') -ne ($expectedProducts -join ',')) {
    Save-P09Evidence ([ordered]@{
        schema_version = 1
        result = 'FAIL'
        executed_at_utc = [DateTime]::UtcNow.ToString('o')
        api_base_url = $ApiBaseUrl
        request = [ordered]@{ method = 'POST'; path = '/api/sales'; http_status = $saleResponse.StatusCode; idempotency_key = $script:IdempotencyKey }
        verification = [ordered]@{
            method = 'GET'; path = $detailPath; http_status = $detailResponse.StatusCode
            odoo_order_id = [string]$detail.externalId; sale_status = [string]$detail.status
            line_count = $detailLines.Count; product_ids = @($detailLines | ForEach-Object { $_.productId })
            payment_method_id = $detail.paymentMethodId; expected_payment_method_id = [long]$cashMethod.id
        }
        note = 'El detalle no coincide con los criterios de P09; la evidencia no declara la prueba aprobada.'
    })
    throw "El detalle no cumple los criterios de P09. Evidencia: $EvidenceFile"
}

$evidence = [ordered]@{
    schema_version = 1
    result = 'PASS'
    executed_at_utc = [DateTime]::UtcNow.ToString('o')
    flow = 'ASP.NET Core API -> OdooGateway -> Odoo JSON-2'
    api_base_url = $ApiBaseUrl
    request = [ordered]@{ method = 'POST'; path = '/api/sales'; http_status = $saleResponse.StatusCode; idempotency_key = $script:IdempotencyKey }
    verification = [ordered]@{
        method = 'GET'
        path = $detailPath
        http_status = $detailResponse.StatusCode
        odoo_order_id = [string]$detail.externalId
        sale_status = [string]$detail.status
        total = [decimal]$detail.total
        line_count = $detailLines.Count
        product_ids = @($detailLines | ForEach-Object { [long]$_.productId })
        payment_method_id = [long]$detail.paymentMethodId
        payment_method = [string]$cashMethod.name
    }
    note = 'Evidencia sanitizada; no contiene contraseñas, API keys, tokens ni cookies.'
}
Save-P09Evidence $evidence
Write-Host "P09 API PASS. Orden Odoo $($detail.externalId), 2 líneas, efectivo. Evidencia: $EvidenceFile"

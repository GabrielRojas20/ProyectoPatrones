# Backend ASP.NET Core

Implementación del Frente B (Vaca) en .NET 10 con dependencias `Api -> Infrastructure/Application -> Domain`.

## Ejecutar

1. Configure variables usando `.env.example` como referencia. ASP.NET Core usa doble guion bajo para secciones (`Auth__JwtSecret`). Puede cargar el archivo local con `infra/odoo/Load-LocalSettings.ps1`.
2. Genere el hash PBKDF2 del operador con el comando de la siguiente sección.
3. Desde `backend`, ejecute `dotnet restore`, `dotnet test` y `dotnet run --project src/ProyectoPatrones.Api`.
4. Consulte `/openapi/v1.json` para revisar el contrato OpenAPI.

El API exige `Auth__JwtSecret`, `Auth__OperatorPasswordHash`, `Odoo__BaseUrl`, `Odoo__Database` y `Odoo__ApiKey`; ninguno se incluye en Git. JSON-2 autentica con la API key del usuario técnico mediante `Authorization: Bearer`, por lo que el nombre del usuario no se transmite en cada llamada.

El composition root registra `OdooGateway -> IOdooJson2Client -> OdooJson2Client`. Las consultas usan `search_read`/`search_count` y la venta usa `pos.order.sync_from_ui`. Un timeout o una respuesta ambigua produce `502` y nunca se informa éxito. La comprobación P09 debe ejecutarse en la instancia Odoo del equipo: compilar o usar dobles no sustituye esa evidencia.

Para iniciar en PowerShell después de crear `.env` local:

```powershell
.\infra\odoo\Load-LocalSettings.ps1
dotnet run --project backend/src/ProyectoPatrones.Api
```

Los orígenes CORS se configuran con `Cors__AllowedOrigins__0`, `Cors__AllowedOrigins__1`, etc. Los valores de desarrollo incluidos son `localhost:5173`, `localhost:5174` y `localhost:5500`; producción debe declarar únicamente sus orígenes HTTPS reales.

## Hash de contraseña

PowerShell (reemplace la contraseña solo en su terminal; no la guarde en Git):

```powershell
$password = Read-Host -AsSecureString
$plain = [System.Net.NetworkCredential]::new('', $password).Password
$salt = [Security.Cryptography.RandomNumberGenerator]::GetBytes(16)
$hash = [Security.Cryptography.Rfc2898DeriveBytes]::Pbkdf2($plain, $salt, 210000, [Security.Cryptography.HashAlgorithmName]::SHA256, 32)
"210000.$([Convert]::ToBase64String($salt)).$([Convert]::ToBase64String($hash))"
```

## Contratos

- `POST /api/auth/login` (público).
- `GET /api/products?name=&code=&page=1&pageSize=10`.
- `GET /api/pos/sessions/active`.
- `POST /api/sales`, con encabezado obligatorio `Idempotency-Key`.
- `GET /api/sales?name=&id=&page=1&pageSize=10`.
- `GET /api/sales/{id}`.

Todos salvo login requieren JWT. Los errores usan `ProblemDetails` con `code` y `traceId`. Las validaciones cubren fecha actual, cédula, correo, nombre, sesión, pago, disponibilidad, precio vigente, cantidades, descuento y confirmación real.

## Idempotencia

Las respuestas confirmadas se conservan en `Idempotency__FilePath` para sobrevivir reinicios. La misma clave y el mismo cuerpo devuelven el resultado anterior; la misma clave con otro cuerpo devuelve `409 idempotency.key_reused`. El archivo local no se versiona. Para despliegues con varias réplicas debe sustituirse por un almacén SQL compartido o una operación atómica equivalente.

## Pruebas

`dotnet test` cubre P01-P06, conflicto de idempotencia, precio, política de descuento (P10), JWT obligatorio, CORS, `ProblemDetails`, arranque del host, registro del gateway real y contratos JSON-2. P09 requiere Odoo real y no se sustituye por un mock.

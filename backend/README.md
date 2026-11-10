# Backend ASP.NET Core

Implementación del Frente B (Vaca) en .NET 10 con dependencias `Api -> Infrastructure/Application -> Domain`.

## Ejecutar

1. Configure variables usando `.env.example` como referencia. ASP.NET Core usa doble guion bajo para secciones (`Auth__JwtSecret`).
2. Genere el hash PBKDF2 del operador con el comando de la siguiente sección.
3. Desde `backend`, ejecute `dotnet restore`, `dotnet test` y `dotnet run --project src/ProyectoPatrones.Api`.
4. Consulte `/openapi/v1.json` para revisar el contrato OpenAPI.

El API exige `Auth__JwtSecret` al arrancar y el inicio de sesión requiere `Auth__OperatorPasswordHash`; ninguno se incluye en Git. El puerto `IOdooGateway` está conectado deliberadamente a `UnconfiguredOdooGateway`: devuelve `502` hasta que el adaptador real asignado al frente de integración se registre. No se simulan productos ni ventas como si vinieran de Odoo.

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

## Pruebas

`dotnet test` cubre P01-P06, verificación de precio e intercambio de política de descuento (P10). P09 requiere Odoo real y pertenece al frente de integración; no se sustituye por un mock.

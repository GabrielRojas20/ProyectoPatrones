# Backend ASP.NET Core

Implementar ASP.NET Core Web API con Clean Architecture reconocible:

- `Domain`: entidades, valores y reglas puras.
- `Application`: casos de uso, puertos, DTOs y validadores.
- `Infrastructure`: cliente Odoo JSON-2, configuración, idempotencia y adaptadores.
- `Presentation`: endpoints REST, autenticación y ProblemDetails.

Contratos iniciales: `POST /api/auth/login`, `GET /api/products`, `GET /api/pos/sessions/active`, `POST /api/sales`, `GET /api/sales`, `GET /api/sales/{id}`.

## Host API

El host se encuentra en `src/ProyectoPatrones.Api` y expone los contratos de autenticación, productos, sesión, ventas, historial y detalle. Por defecto usa el adaptador Odoo real; `Pos__Mode=Demo` habilita explícitamente el adaptador local para pruebas aisladas.

```powershell
./scripts/Run-Local.ps1
```

URL: `http://localhost:5080`. Credenciales demo: `operator` / `pos-demo`.

## Adaptador Odoo JSON-2

El proyecto `src/ProyectoPatrones.Infrastructure.Odoo` contiene el cliente HTTP tipado y la validación de configuración. Todavía no se conectó a un caso de uso de venta porque el contrato funcional y la firma real de `pos.order` deben confirmarse en la instancia del proyecto.

Registra el adaptador desde el composition root de la API:

```csharp
var odooOptions = builder.Configuration.GetSection(OdooJson2Options.SectionName)
    .Get<OdooJson2Options>() ?? new OdooJson2Options();
builder.Services.AddOdooJson2(odooOptions);
```

El `.env` solo lo lee Docker Compose. Para cargar la misma configuración en el proceso de ASP.NET Core, ejecuta primero `infra/odoo/Load-LocalSettings.ps1`; el script establece las variables del proceso sin ejecutar su contenido. La API key debe pertenecer a un usuario de integración de Odoo con permisos mínimos. Nunca se registra su contenido. Una `OdooIntegrationException` con `OutcomeMayHaveBeenApplied == true` debe convertirse en estado de conciliación, nunca en respuesta de venta exitosa.

## Responsabilidades por capa

- Domain/Application: validar productos, cantidades, sesión activa, pago, descuentos e idempotencia.
- Infrastructure: hablar con Odoo y reportar de forma explícita respuesta confirmada, rechazo o resultado incierto.
- Presentation: traducir errores a ProblemDetails sin devolver trazas o credenciales.

## Compilación

```powershell
dotnet build backend/src/ProyectoPatrones.Infrastructure.Odoo/ProyectoPatrones.Infrastructure.Odoo.csproj --configuration Release
```

La integración validada utiliza `pos.order.sync_from_ui`, conserva resultados inciertos y exige `Idempotency-Key`. La evidencia P09 está registrada en la matriz de cumplimiento.

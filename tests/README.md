# Pruebas

## Verificaciones locales del adaptador

No requieren NuGet adicional ni una instancia Odoo:

```powershell
dotnet run --project tests/ProyectoPatrones.Odoo.ContractChecks/ProyectoPatrones.Odoo.ContractChecks.csproj --configuration Release
```

Comprueban ruta y verbo JSON-2, encabezados de autenticacion y base de datos, serializacion, rechazo HTTP, incertidumbre en errores 5xx y timeout, rutas manipuladas y configuracion obligatoria. Usan un HttpMessageHandler falso y no demuestran una venta registrada.

## Estado de la matriz

| ID | Caso | Estado |
|---|---|---|
| P01–P04 | Reglas previas al envío | Verificadas en Application |
| P05 | Error o timeout Odoo sin éxito falso | Verificado en el adaptador |
| P06 | Idempotencia | Verificada en Application y contra Odoo real |
| P07–P08 | Carrito, total y error comprensible | Verificados en frontend |
| P09 | Venta real y efectivo en Odoo | Orden `260-2-000002`, estado `paid`, IVA y pago verificados |
| P10 | Cambio controlado de política | Verificado con Strategy intercambiable |

P09 se ejecutó además de las suites automatizadas: la repetición de la clave devolvió el mismo ID Odoo `6` sin crear otra orden.

## Todas las verificaciones automatizadas

```powershell
dotnet run --project tests/ProyectoPatrones.Application.Checks/ProyectoPatrones.Application.Checks.csproj --configuration Release
dotnet run --project tests/ProyectoPatrones.Odoo.ContractChecks/ProyectoPatrones.Odoo.ContractChecks.csproj --configuration Release
node frontend/pos/tests/frontend-checks.mjs
```

Resultado actual: 10 verificaciones de Application, 8 de contrato Odoo y 5 de frontend; 23/23 en total.

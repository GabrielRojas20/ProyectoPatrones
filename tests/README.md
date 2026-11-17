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
| P01 | Venta sin productos | Pendiente de Application |
| P02 | Cantidad cero o negativa | Pendiente de Application |
| P03 | Sesion POS cerrada | Configuracion Odoo reproducible; bloqueo pendiente de Application |
| P04 | Producto no habilitado | Pendiente de Application/Odoo |
| P05 | Error o timeout Odoo sin exito falso | Verificado parcialmente en el adaptador |
| P06 | Idempotencia | Pendiente de persistencia y caso de uso |
| P07 | Carrito y total | Pendiente de Application/frontend |
| P08 | Error de API en frontend | Pendiente de frontend |
| P09 | Venta real de dos productos y efectivo en Odoo | Aprobado en Odoo 20 local; evidencia sanitizada en `infra/odoo/evidence/p09-latest.json` |
| P10 | Cambio controlado de politica o integracion | Pendiente de acordar limites e integrar flujo |

P09 es una prueba de integracion real y no se reemplaza por un mock. Se ejecuta con:

```powershell
./infra/odoo/Invoke-OdooQualityChecks.ps1
```

El script vuelve a consultar la orden, exige dos lineas, un pago en efectivo y un estado pagado,
y solo entonces escribe la evidencia sanitizada.

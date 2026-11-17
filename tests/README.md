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
| P09 | Venta real de dos productos y efectivo mediante API y JSON-2 | Pendiente de respuesta HTTP y trazas sanitizadas del flujo completo |
| P10 | Cambio controlado de politica o integracion | Pendiente de acordar limites e integrar flujo |

La preparación de infraestructura se comprueba con:

```powershell
./infra/odoo/Invoke-OdooQualityChecks.ps1
```

El script crea una venta directamente mediante el ORM, vuelve a consultar la orden y exige dos
lineas, un pago en efectivo y un estado pagado. Su evidencia se guarda como
`infra/odoo/evidence/infrastructure-sale-latest.json` y no se presenta como P09.

P09 debe ejecutarse posteriormente mediante `POST /api/sales`, atravesar `OdooGateway` y JSON-2,
y conservar la respuesta HTTP y trazas sanitizadas antes de cambiar su estado a aprobado.

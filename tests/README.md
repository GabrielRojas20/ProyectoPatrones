# Pruebas

## Pruebas del backend ASP.NET Core

Desde la raíz del repositorio, ejecute:

~~~powershell
dotnet test backend/ProyectoPatrones.Backend.slnx --configuration Release
~~~

La matriz identifica las pruebas automatizadas presentes en el código; no afirma que se ejecutaron en cada cambio. Consulte los checks de GitHub Actions o ejecute el comando anterior para confirmar el resultado de la versión actual.

## Verificaciones locales del adaptador Odoo JSON-2

No requieren NuGet adicional ni una instancia Odoo:

~~~powershell
dotnet run --project tests/ProyectoPatrones.Odoo.ContractChecks/ProyectoPatrones.Odoo.ContractChecks.csproj --configuration Release
~~~

Comprueban ruta y verbo JSON-2, encabezados de autenticación y base de datos, serialización, rechazo HTTP, incertidumbre en errores 5xx y timeout, rutas manipuladas y configuración obligatoria. Usan un HttpMessageHandler falso y no demuestran una venta registrada.

## Matriz de cobertura

| ID | Caso | Cobertura automatizada en el repositorio |
|---|---|---|
| P01 | Venta sin productos | Caso de uso: rechaza la solicitud (P01_Rejects_sale_without_products). |
| P02 | Cantidad cero o negativa | Caso de uso: rechaza cantidades no positivas (P02_Rejects_non_positive_quantity). |
| P03 | Sesión POS cerrada | Caso de uso: rechaza una sesión cerrada (P03_Rejects_closed_session). |
| P04 | Producto no disponible | Caso de uso: rechaza el producto antes de enviar la venta (P04_Rejects_unavailable_product_before_sending). |
| P05 | Error o timeout de Odoo sin éxito falso | Prueba del caso de uso ante error de integración y verificaciones de contrato para HTTP/timeout. |
| P06 | Idempotencia | Reutilización de clave devuelve la venta previa; la misma clave con otro cuerpo se rechaza. El almacén de archivo está implementado. |
| P07 | Carrito y total | Pendiente de completar la verificación integrada de frontend y API. |
| P08 | Error de API en frontend | Pendiente de completar la verificación integrada de frontend y API. |
| P09 | Venta real de dos productos y efectivo mediante API y JSON-2 | Pendiente de respuesta HTTP y trazas sanitizadas del flujo completo. |
| P10 | Cambio controlado de política o integración | Prueba un cambio de política de descuento sin modificar el caso de uso (P10_Discount_policy_can_change_without_modifying_use_case). |

La preparación de infraestructura se comprueba con:

~~~powershell
./infra/odoo/Invoke-OdooQualityChecks.ps1
~~~

El script crea una venta directamente mediante el ORM, vuelve a consultar la orden y exige dos líneas, un pago en efectivo y un estado pagado. Su evidencia se guarda como infra/odoo/evidence/infrastructure-sale-latest.json y no se presenta como P09.

P09 se ejecuta mediante `POST /api/sales`, atraviesa `OdooGateway` y JSON-2, y debe conservar la respuesta HTTP y trazas sanitizadas antes de cambiar su estado a aprobado. Con Odoo y la API en ejecución, desde la raíz del repositorio ejecute:

~~~powershell
./infra/odoo/Invoke-P09ApiIntegration.ps1 `
  -ApiBaseUrl "http://localhost:8080"
~~~

Use `http://localhost:8080` when running the script on the API host. For a remote API, pass its
reachable server IP, for example `-ApiBaseUrl "http://192.168.100.37:8080"`; do not use a wildcard
listen address such as `0.0.0.0` as the request destination. The script does not infer its target
from `ASPNETCORE_URLS`.

El ejecutor solicita la contraseña del operador de forma interactiva, consulta los dos productos y la sesión POS, registra la venta con una clave de idempotencia y verifica el detalle devuelto por Odoo a través de la API. Solo produce `result: PASS` si encuentra las dos líneas, la orden confirmada y el método de pago en efectivo. Guarda evidencia sanitizada en `infra/odoo/evidence/p09-api-latest.json`; no incluye credenciales, tokens ni encabezados de autenticación. Si el resultado es incierto, use la clave conservada en la evidencia para conciliar la venta antes de volver a intentarla.

La existencia del script no significa que P09 se haya ejecutado. La matriz solo debe actualizarse después de una ejecución real satisfactoria contra la instancia local de Odoo.

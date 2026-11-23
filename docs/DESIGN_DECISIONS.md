# Decisiones de diseño

## 1. Políticas de descuento intercambiables

- Problema: incorporar descuentos sin modificar el caso de uso de venta.
- Alternativas: condicionales dentro del controlador, tabla de configuración o Strategy.
- Elección: Strategy mediante `IDiscountPolicy`; es una aplicación de patrón de comportamiento.
- Consecuencias: cada política es aislada y comprobable; debe registrarse explícitamente para ser autorizada.
- Código: `ProyectoPatrones.Domain/Models.cs` y registro en `ProyectoPatrones.Api/Program.cs`.
- Evidencia: P10 agrega `TEMP15` sin modificar `PosService`.

## 2. Puerto para el sistema empresarial

- Problema: probar reglas sin depender de Odoo y cambiar el mecanismo de integración si JSON-2 no soporta una operación.
- Alternativas: llamar HTTP desde controladores, repositorio específico de Odoo o Ports and Adapters.
- Elección: `IPosGateway` como puerto de Application, con adaptador demo claramente rotulado y cliente Odoo separado.
- Consecuencias: Application no conoce HTTP; una integración real requiere un adaptador que traduzca el contrato validado de la instancia.
- Código: `ProyectoPatrones.Application/Contracts.cs`, `Infrastructure.Demo` e `Infrastructure.Odoo`.
- Evidencia: P03/P04 usan un doble sin cambiar el caso de uso; las verificaciones Odoo prueban el contrato HTTP.

## 3. Idempotencia antes del adaptador externo

- Problema: un reintento puede crear una segunda venta.
- Alternativas: confiar en el navegador, deduplicar después o reservar una clave antes de enviar.
- Elección: Idempotency Key con estado reservado/completado mediante `IIdempotencyStore`.
- Consecuencias: una misma clave devuelve el resultado anterior; para producción el almacén en memoria debe sustituirse por persistencia compartida y conservar estados inciertos.
- Código: `PosService.CreateSaleAsync` y `MemoryIdempotencyStore`.
- Evidencia: P06 ejecuta dos veces la solicitud y comprueba una sola llamada al gateway.

## 4. Estado y carrito independientes del DOM

- Problema: probar cálculos y cambios de cantidad sin navegador.
- Alternativas: estado directamente en elementos HTML, framework completo o modelo `Cart` independiente.
- Elección: entidad de frontend `Cart` y orquestador `PosStore`; presentación e HTTP dependen de ellos.
- Consecuencias: el flujo es portable y comprobable, con una implementación deliberadamente pequeña.
- Evidencia: P07 corre en Node sin DOM.

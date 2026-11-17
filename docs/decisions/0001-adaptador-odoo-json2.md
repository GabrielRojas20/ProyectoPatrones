# ADR 0001: acceso a Odoo mediante JSON-2

- Estado: aceptada para el adaptador de infraestructura; P09 de integración continúa pendiente.
- Fecha: 2026-11-08.

## Contexto

La API ASP.NET Core debe comunicarse con Odoo sin exponer la API key al navegador. El adaptador tiene que distinguir una respuesta de error de una venta confirmada y soportar cancelación por cierre de la petición.

## Decisión

Se implementa un cliente HTTP tipado que envía POST a `json/2/{modelo}/{método}`, usando `Authorization: Bearer`, `X-Odoo-Database` y JSON. La URL, base, clave y timeout se inyectan desde configuración local. El adaptador no define todavía el flujo de creación de `pos.order`: ese método y su payload deben confirmarse contra la instancia real y el contrato del equipo antes de usarse en una venta.

Los errores 4xx se reportan como rechazados. Los 5xx, los timeouts, los errores de transporte y las respuestas JSON ilegibles se marcan como resultado incierto cuando el servidor pudo haber procesado la solicitud. No se reintentan automáticamente escrituras: primero se debe consultar el estado o aplicar idempotencia desde la capa de aplicación.

## Consecuencias

- El frontend solo conoce la API del proyecto; la clave de Odoo se queda en el servidor.
- La capa de aplicación debe impedir que un resultado incierto se convierta en una venta exitosa y debe resolver reintentos con una clave de idempotencia.
- La comprobación ORM confirma que la configuración POS admite una venta de dos productos y efectivo, pero no es P09 ni valida el payload HTTP del backend.
- La firma y respuesta JSON-2 de la venta deben mantenerse cubiertas por el gateway de Application/Infrastructure y su prueba de integración.

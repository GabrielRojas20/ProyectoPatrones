# ADR 0003: idempotencia y resultado de operaciones

- Estado: propuesta de integración; el mecanismo persistente se debe implementar en Application/Infrastructure.
- Fecha: 2026-11-08.

## Propuesta

Cada solicitud de venta debe llevar una clave de idempotencia generada por el cliente y validada por la API. La API debe asociar la clave al usuario y a una huella del contenido; misma clave con contenido distinto se rechaza. Una repetición con la misma clave devuelve el resultado previamente guardado.

Los errores de transporte y los HTTP 5xx de Odoo no confirman si la operación fue aplicada. La API debe conservar el estado como pendiente de conciliación y consultar Odoo antes de permitir otro intento de escritura. No se deben automatizar reintentos de una operación de venta hasta implementar esa conciliación.

## Pendientes

Se debe definir almacenamiento durable, expiración, índice único, retención, operación de conciliación y qué identificador de Odoo permite localizar la venta. El adaptador actual únicamente expone la incertidumbre; no implementa por sí solo persistencia o deduplicación.

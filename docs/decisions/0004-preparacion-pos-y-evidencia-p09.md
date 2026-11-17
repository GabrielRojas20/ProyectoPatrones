# ADR 0004: preparación reproducible del POS y evidencia P09

- Estado: aceptada.
- Fecha: 2026-11-16.

## Contexto

La preparación manual de Odoo no deja una prueba repetible de productos, impuesto, efectivo y
sesión. Levantar contenedores tampoco demuestra que una venta haya quedado registrada. La evidencia
del Pull Request debe ser útil sin divulgar secretos locales.

## Decisión

Se mantiene Odoo 20 Community y PostgreSQL 16 en Docker Compose. Un script PowerShell instala
`point_of_sale`, espera los healthchecks y ejecuta dos scripts mediante `odoo shell`:

1. `bootstrap_pos.py` crea o actualiza el impuesto, dos productos vendibles, diario, pago en
   efectivo, configuración POS y sesión abierta.
2. `p09_sale.py` crea una orden con ambos productos, registra un pago en efectivo, confirma la
   orden y vuelve a consultarla antes de declarar el resultado aprobado.

La evidencia versionable contiene solo base, IDs técnicos, referencia POS, cantidad de líneas,
total y estado. Las credenciales se leen del `.env` ignorado por Git y no se imprimen en la
evidencia.

## Consecuencias

- El entorno de Lara se puede reconstruir y verificar con un comando.
- P09 demuestra una operación persistida en Odoo, pero no reemplaza las pruebas del adaptador
  JSON-2 ni del caso de uso del backend.
- Cada ejecución crea una nueva venta; la idempotencia de la API sigue siendo responsabilidad de
  Application/Infrastructure.
- Los IDs son locales y pueden cambiar al recrear volúmenes, por lo que la evidencia se interpreta
  junto con su estado y sus invariantes, no como identificadores permanentes.

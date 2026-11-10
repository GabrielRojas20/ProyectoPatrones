# ADR 0002: límites para descuentos

- Estado: propuesta pendiente de acuerdo con el equipo.
- Fecha: 2026-11-08.

## Propuesta

La API debe validar el descuento antes de enviar la venta a Odoo, mantener el total calculado en servidor y registrar el motivo del descuento. El cliente no debe poder elegir un porcentaje arbitrario ni alterar el total final enviando un importe calculado por el navegador.

## Pendientes de decisión

El porcentaje máximo, los roles autorizados, los redondeos, los descuentos acumulables y la forma de reflejarlos en Odoo deben aprobarse con el equipo y quedar implementados en `Application`. Esta propuesta no establece límites numéricos ni se presenta como política ya implementada.

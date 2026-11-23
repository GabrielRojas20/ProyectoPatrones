# Matriz de cumplimiento

Revisión basada en la especificación académica del POS Odoo 20.

| Grupo | Estado | Evidencia / límite |
|---|---|---|
| RF-01–02 | Cumplido | Catálogo, código, precio y disponibilidad se consultan mediante JSON-2 sobre Odoo 20. |
| RF-03–05 | Cumplido | Carrito editable, cantidades positivas, eliminación, subtotales y total; P01, P02 y P07. |
| RF-06–08 | Cumplido | Strategy `IDiscountPolicy`, política DOCENTE10 y rechazo de códigos no registrados. |
| RF-09–12 | Cumplido para efectivo | `sync_from_ui` registra venta y pago real, devuelve referencia y estado Odoo. Transferencia/tarjeta se muestran bloqueadas hasta configurarse en la caja. |
| RF-13–14 | Cumplido | Historial y detalle consultan `pos.order` real. |
| RF-15–17 | Cumplido | Validaciones, ProblemDetails, idempotencia, timeout y resultado incierto; P01–P08. |
| RN-01–08 | Cumplido | Reglas verificadas en Domain/Application y confirmación real en Odoo. |
| RNF-01–02 | Cumplido | Backend Domain/Application/Infrastructure/Presentation; frontend Domain/Application/Infrastructure/Presentation. |
| RNF-03–05 | Cumplido | Cuatro decisiones justificadas, puertos, Strategy, responsabilidades pequeñas. |
| RNF-06–08 | Cumplido | Checks ejecutables de Application, frontend y contrato Odoo; errores consistentes. |
| RNF-09 | Parcial | Secretos y API key quedan fuera de Git y la entrada se valida. Falta reemplazar el token académico por JWT/RBAC completo antes de producción. |
| RNF-10–12 | Cumplido | Contratos, scripts e interoperabilidad real reproducibles. |
| P01–P08, P10 | Cumplido | 23 verificaciones automatizadas distribuidas en tres suites. |
| P09 | Cumplido | Orden real `260-2-000002`, estado `paid`, base `$2.17`, IVA `$0.33`, total/pago `$2.50`; la repetición de la clave devolvió el mismo ID `6`. |

## Conclusión

La solución ejecuta el flujo completo contra Odoo local. Permanecen como evolución el JWT/RBAC persistente, SQLite/FTS5 y métodos de pago adicionales; la interfaz los identifica sin simularlos.

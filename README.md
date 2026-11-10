# ProyectoPatrones

POS web integrado con Odoo 20 Community para la asignatura Patrones de Software.

## Objetivo

Construir un frontend completo que consuma una API REST en ASP.NET Core. La API concentra autenticación, validaciones, políticas de descuento, idempotencia, manejo de errores y la integración con Odoo. El frontend nunca accede directamente a Odoo ni a PostgreSQL.

## Equipo

- Gabriel — frontend POS, experiencia, validaciones y paginación.
- Vaca — backend ASP.NET Core, casos de uso y reglas de negocio.
- Lara — integración Odoo, infraestructura, pruebas y calidad.

El reparto detallado, los contratos, la matriz de pruebas y el flujo de ramas están en `docs/Plan_ProyectoPatrones_Gabriel_Vaca_Lara.docx` y su PDF.

## Estructura inicial

```text
backend/       API ASP.NET Core y capas Domain/Application/Infrastructure/Presentation
frontend/      Aplicación web y adaptadores HTTP
infra/odoo/    Docker Compose de Odoo y PostgreSQL
tests/         Pruebas unitarias, contrato e integración
docs/          Plan, especificación y decisiones de diseño
```

## Reglas de colaboración

- `main` y `develop` están protegidas.
- Todo cambio entra por Pull Request.
- Cada PR requiere dos aprobaciones de integrantes distintos del autor.
- Los checks deben estar verdes y la rama actualizada antes del merge.
- No se aceptan secretos, API keys ni contraseñas en Git.
- Usar ramas `feature/<integrante>-<tema>`, `fix/<integrante>-<tema>`, `test/<integrante>-<tema>` o `docs/<integrante>-<tema>`.

## Preparación local

1. Instalar .NET 10 SDK, Node.js, Docker y Git.
2. Copiar `.env.example` como `.env` y completar valores solo localmente.
3. Levantar Odoo/PostgreSQL desde `infra/odoo`; ver sus instrucciones en `infra/odoo/README.md`.
4. Configurar productos, impuestos, punto de venta, sesión y pago efectivo en Odoo.
5. Ejecutar la API y el frontend según `backend/README.md` y `frontend/README.md`.
6. Ejecutar las pruebas con los comandos publicados en `tests/README.md`.

## Estado de integración

La integración real de ventas y pagos no se considera comprobada hasta registrar una operación contra la instancia local de Odoo y conservar respuesta HTTP, trazas sanitizadas e identificadores verificables. No se debe presentar un simulador como una operación real.

## Aporte de Lara: Odoo, infraestructura y calidad

- Adaptador HTTP tipado para Odoo JSON-2 con API key de servidor, validación de configuración y errores con resultado incierto para timeouts/HTTP 5xx.
- Docker Compose de Odoo 20 y PostgreSQL 16 con variables locales y espera de salud de PostgreSQL.
- Verificaciones locales del contrato HTTP y workflow de GitHub Actions.
- Decisiones documentadas sobre integración, descuentos e idempotencia. Las políticas de descuentos e idempotencia permanecen como propuestas pendientes de implementación en Application.
- La venta completa, el registro de pago y P09 requieren el host API, el contrato validado por el equipo y una instancia real de Odoo.

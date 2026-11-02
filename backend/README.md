# Backend

Implementar ASP.NET Core Web API con Clean Architecture reconocible:

- `Domain`: entidades, valores y reglas puras.
- `Application`: casos de uso, puertos, DTOs y validadores.
- `Infrastructure`: cliente Odoo JSON-2, configuración, idempotencia y adaptadores.
- `Presentation`: endpoints REST, autenticación y ProblemDetails.

Contratos iniciales: `POST /api/auth/login`, `GET /api/products`, `GET /api/pos/sessions/active`, `POST /api/sales`, `GET /api/sales`, `GET /api/sales/{id}`.

# Frente A · Frontends POS

Tres clientes independientes consumen la misma API ASP.NET Core: React, Vue y HTML/JS puro. Ninguno accede directamente a Odoo o SQL Server.

## Arranque

Backend esperado: `http://localhost:8080` (o la URL/IP real del servidor Linux).

```bash
cd frontend/react && npm install && cp .env.example .env && npm run dev
cd frontend/vue && npm install && cp .env.example .env && npm run dev
cd frontend/html && python3 -m http.server 5500
```

Para HTML, editar `config.js` y colocar la URL del backend. En React/Vue, editar `.env` con `VITE_API_BASE`. El API debe permitir CORS para los puertos 5173, 5174 y 5500.

## Arquitectura

- `shared/api.js`: adaptador HTTP aislado de las vistas.
- `shared/validation.js`: reglas de entrada compartidas; límite de 40 caracteres, bloqueo de pegado y validaciones de cédula/correo.
- `react/src`, `vue/src`, `html`: presentación y estado visual de cada implementación.

Los tres clientes implementan login, catálogo paginado, filtros dinámicos separados, carrito, sesión POS, venta con `Idempotency-Key`, historial paginado y detalle. Los filtros se disparan mientras se escribe y la API siempre recibe `pageSize=10`.

## Verificación local

```bash
cd frontend/react && npm run build
cd frontend/vue && npm run build
node --check frontend/html/app.js
```

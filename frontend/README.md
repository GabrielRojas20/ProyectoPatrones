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

- `shared/api.js`: adaptador HTTP aislado de las vistas; recibe y conserva la clave de idempotencia del intento de venta.
- `shared/validation.js`: reglas de entrada compartidas; límite de 40 caracteres, limpieza de pegado y validaciones de cédula/correo.
- `react/src`, `vue/src`, `html`: presentación y estado visual de cada implementación.

Los tres clientes implementan login, catálogo paginado, filtros dinámicos separados, carrito, sesión POS, venta con `Idempotency-Key`, historial paginado y detalle. Los filtros se disparan mientras se escribe y la API siempre recibe `pageSize=10`.

Cada interfaz diferencia carga, lista vacía, error y resultado. El botón de confirmación se bloquea mientras se crea una venta; un reintento por timeout conserva la misma clave, mientras que una venta confirmada inicia el siguiente intento con una nueva clave.

## Verificación local

```bash
cd frontend/react && npm run build
cd frontend/vue && npm run build
cd ../..
node --check frontend/html/app.js
node frontend/tests/validation.test.mjs
```

La prueba de validaciones cubre límite de 40 caracteres, letras, números, correo, cédula y fecha. La validación de integración debe ejecutarse después con la API, Odoo y CORS reales configurados.

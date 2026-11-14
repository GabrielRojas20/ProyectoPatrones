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
node frontend/tests/review-functional.test.mjs
```

La prueba de validaciones cubre límite de 40 caracteres, letras, números, correo, cédula y fecha. La prueba funcional cubre 409 sin sesión, 502 incierto, repetición con la misma clave y payload, segundo método de pago y fecha local de Ecuador. La validación de integración debe ejecutarse después con la API, Odoo y CORS reales configurados.


## Manejo de sesión POS y resultados inciertos

Los tres clientes consultan `/api/pos/sessions/active` antes de cargar el catálogo. Si la API responde `409`, muestran la vista **Sin sesión POS activa**, bloquean el catálogo y el checkout, y ofrecen reintentar la consulta o cerrar sesión.

Una venta no confirmada se conserva como un intento congelado. Las respuestas `502`, `504`, los timeouts y los errores de red conservan el mismo payload y la misma cabecera `Idempotency-Key` para el reintento. Solo después de una confirmación exitosa se libera el intento. El descarte muestra una advertencia porque Odoo pudo haber registrado la venta.

La prueba `frontend/tests/review-functional.test.mjs` cubre sesión inexistente, bloqueo de sesión, `502`, `504`, pérdida de red, reintento con la misma clave, método de pago y fecha local.

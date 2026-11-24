# Reglas de negocio del punto de venta

## Datos de cliente

- El nombre es obligatorio, admite hasta 35 caracteres y solo letras Unicode y espacios. No admite números ni signos.
- El correo es obligatorio, admite hasta 35 caracteres y valida una estructura de correo con `@`, dominio y extensión. Acepta signos válidos en la dirección, como `+`, `.`, `_` y `-`.
- La identificación admite 10 dígitos para cédula o 13 para RUC.
- La persona debe existir y estar activa en Odoo antes de crear la venta.
- La búsqueda acepta nombres y apellidos, solicita al menos dos letras y muestra 10 resultados por página. El cuadro permanece abierto al hacer clic fuera; se cierra con el botón o al elegir una persona.

## Catálogo y carrito

- El backend comprueba que cada producto exista, esté activo y habilitado para el POS.
- Antes de confirmar, vuelve a consultar el precio y las existencias en Odoo. Si cambiaron, bloquea la venta y devuelve un mensaje en español para actualizar el carrito.
- No se permite agregar productos sin existencias. La cantidad por producto debe estar entre 1 y 40 unidades y no puede superar las existencias disponibles.
- El carrito no puede estar vacío y quitar una línea requiere confirmación.
- El descuento debe respetar la política configurada; el valor predeterminado es 20 % como máximo.

## Confirmación y concurrencia

- Se exige sesión POS abierta, método de pago habilitado y fecha de operación del día del servidor.
- Las confirmaciones se serializan dentro del proceso de la API y se vuelve a consultar el inventario antes de crear la orden. Para terminales conectados a una sola instancia de esta API, dos ventas del último artículo no pueden confirmarse ambas: la segunda recibe `sale.insufficient_stock` y debe actualizar el carrito.
- La clave de idempotencia conserva el mismo resultado al reintentar un envío y evita duplicar una venta ya confirmada.
- La exclusión de concurrencia es local al proceso de la API. Si el despliegue se amplía a varias instancias de backend o hay ventas paralelas hechas directamente en Odoo, se necesita coordinación o reserva atómica de inventario en el servidor Odoo para garantizar el mismo bloqueo entre esos procesos.

## Interfaz y consulta

- Los filtros de producto permiten buscar nombres con letras y números para no excluir modelos como `X15 Pro+`.
- Los resultados del catálogo, clientes e historial están paginados.
- Los mensajes que se muestran al operador están en español; los códigos técnicos se conservan para la lógica interna y el diagnóstico.

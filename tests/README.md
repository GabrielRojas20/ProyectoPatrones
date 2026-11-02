# Pruebas

P01 venta sin productos; P02 cantidad cero/negativa; P03 sesión POS cerrada; P04 producto no habilitado; P05 error/timeout Odoo sin éxito falso; P06 idempotencia; P07 carrito y total; P08 error de API en frontend; P09 venta real de dos productos y efectivo en Odoo; P10 cambio controlado de política o integración.

Las pruebas unitarias usan dobles para Application. P09 es una prueba de integración real y no se reemplaza por un mock.

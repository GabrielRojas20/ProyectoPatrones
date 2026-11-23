# Frontend

Construir una aplicación web completa con Clean Architecture en el cliente. El cliente HTTP debe estar aislado de las vistas. Debe implementar catálogo, búsqueda dinámica por nombre e ID/código, carrito, descuentos autorizados, confirmación, método de pago, historial, detalle y paginación de 10 registros.

Validaciones obligatorias: máximo 40 caracteres, letras/números según el campo, cédula de exactamente 10 dígitos, correo con una sola extensión final, fecha del día actual, bloqueo de teclado/pegado y mensajes claros.

## Ejecución

El frontend funcional está en `frontend/pos` y separa Domain, Application, Infrastructure y Presentation.

```powershell
python -m http.server 5173 --directory frontend/pos
```

Abrir `http://localhost:5173`. Requiere la API en `http://localhost:5080`.

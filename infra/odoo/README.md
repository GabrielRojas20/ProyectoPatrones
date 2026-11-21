# Infraestructura local de Odoo

Esta configuración levanta Odoo 20 Community y PostgreSQL 16 en contenedores para desarrollo local. No configura TLS, copias de seguridad ni endurecimiento de producción.

## Requisitos

- Docker Desktop iniciado con contenedores Linux.
- Docker Compose v2.
- Archivo `.env` en la raíz del repositorio.

## Preparación y comprobación de infraestructura en PowerShell

Desde la raíz del repositorio:

```powershell
Copy-Item .env.example .env
# Edita .env y reemplaza ODOO_POSTGRES_PASSWORD, Odoo__ApiKey y JWT_SECRET.
./infra/odoo/Invoke-OdooQualityChecks.ps1
```

El script valida el Compose, levanta PostgreSQL, crea o actualiza la base indicada en
`Odoo__Database`, instala `point_of_sale`, espera la salud de Odoo y configura de forma
idempotente:

- impuesto de venta IVA POS 15 %;
- productos vendibles `POS-CAFE` y `POS-SANDWICH`;
- diario y método de pago `Efectivo Proyecto`;
- punto de venta `POS ProyectoPatrones`;
- sesión POS abierta.

Finalmente registra directamente mediante el ORM una venta de comprobación y guarda evidencia
sanitizada en `infra/odoo/evidence/infrastructure-sale-latest.json`. El archivo contiene únicamente
IDs técnicos, total, estado y número de líneas; nunca contiene contraseñas, claves API, tokens ni
cookies.

Esta comprobación demuestra que Odoo, el POS y el pago en efectivo están preparados. No atraviesa
la API ASP.NET Core ni JSON-2 y, por tanto, no se considera la prueba P09 de integración. El proceso
es repetible y cada ejecución crea una venta de infraestructura con una referencia aleatoria.
Abre `http://localhost:8069` para revisar la instancia. No reutilices contraseñas de ejemplo.

Para levantar solamente los servicios, sin preparar datos ni crear una venta:

```powershell
docker compose --env-file .env -f infra/odoo/docker-compose.yml up -d
docker compose --env-file .env -f infra/odoo/docker-compose.yml ps
```

## Comprobación y apagado

```powershell
docker compose --env-file .env -f infra/odoo/docker-compose.yml logs --tail 100 odoo
docker compose --env-file .env -f infra/odoo/docker-compose.yml down
```

`down` conserva los volúmenes. Para borrar deliberadamente los datos locales de Odoo/PostgreSQL, se puede usar `down -v`.

Para cargar la configuración local en el proceso API desde PowerShell, ejecuta `infra/odoo/Load-LocalSettings.ps1` y luego inicia el host ASP.NET Core:

```powershell
.\infra\odoo\Load-LocalSettings.ps1
dotnet run --project backend/src/ProyectoPatrones.Api
```

Los valores reales solo se configuran localmente. La API key debe pertenecer al usuario de integración de Odoo y no es su contraseña de inicio de sesión.

## Estado de P09

P09 permanece pendiente. Solo podrá aprobarse cuando una solicitud pase por la API ASP.NET Core,
el gateway y Odoo JSON-2, y se conserven la respuesta HTTP y las trazas sanitizadas. La venta ORM
de infraestructura no sustituye esa validación ni las verificaciones de contrato existentes.

Con Odoo preparado y la API en ejecución, abre otra terminal en la raíz del repositorio y ejecuta:

```powershell
.\infra\odoo\Invoke-P09ApiIntegration.ps1 `
  -ApiBaseUrl "http://localhost:8080"
```

El script usa `http://localhost:8080` por defecto y no reutiliza `ASPNETCORE_URLS`, que puede
contener una dirección de escucha como `http://0.0.0.0:8080`. Para ejecutarlo desde otra máquina,
indica la IP real del servidor, por ejemplo:

```powershell
.\infra\odoo\Invoke-P09ApiIntegration.ps1 `
  -ApiBaseUrl "http://192.168.100.37:8080"
```

El script solicita la contraseña del operador sin guardarla, inicia sesión, consulta los productos
`POS-CAFE` y `POS-SANDWICH`, verifica la sesión y el método `Efectivo Proyecto`, registra una venta
por `POST /api/sales` y consulta su detalle mediante la API. Solo escribe resultado `PASS` si
confirma la orden en Odoo, las dos líneas y el pago esperado. La evidencia se guarda en
`infra/odoo/evidence/p09-api-latest.json`; si la respuesta deja el resultado incierto, conserva
la clave de idempotencia para poder conciliar la operación antes de cualquier reintento.

## Seguridad y diagnóstico

- PostgreSQL no publica ningún puerto al host.
- La clave JSON-2 pertenece al usuario de integración y solo se carga en el backend.
- El Compose exige la contraseña de PostgreSQL y no contiene valores por defecto secretos.
- Ante un fallo, revisa `docker compose --env-file .env -f infra/odoo/docker-compose.yml logs --tail 100 odoo`.
- No adjuntes `.env`, encabezados HTTP, volcados de base ni logs sin sanitizar al Pull Request.

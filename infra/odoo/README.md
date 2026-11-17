# Infraestructura local de Odoo

Esta configuración levanta Odoo 20 Community y PostgreSQL 16 en contenedores para desarrollo local. No configura TLS, copias de seguridad ni endurecimiento de producción.

## Requisitos

- Docker Desktop iniciado con contenedores Linux.
- Docker Compose v2.
- Archivo `.env` en la raíz del repositorio.

## Preparación completa y prueba P09 en PowerShell

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

Finalmente registra una venta pagada con ambos productos y guarda evidencia sanitizada en
`infra/odoo/evidence/p09-latest.json`. El archivo contiene únicamente IDs técnicos, total,
estado y número de líneas; nunca contiene contraseñas, claves API, tokens ni cookies.

El proceso es repetible sobre una base ya inicializada. Cada ejecución de P09 crea una venta
nueva con una referencia aleatoria para que la evidencia corresponda a una operación real.
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

Para cargar la configuración local en el proceso API desde PowerShell, ejecuta `infra/odoo/Load-LocalSettings.ps1` y luego inicia el host ASP.NET Core cuando se agregue al repositorio. Los valores reales solo se configuran localmente. La API key debe pertenecer al usuario de integración de Odoo y no es su contraseña de inicio de sesión.

## Criterio de aprobación P09

Levantar los contenedores no demuestra que una venta se haya registrado. P09 se aprueba solo
cuando Odoo conserva una orden en estado `paid`, `done` o `invoiced`, exactamente dos líneas y
un pago mediante el método de efectivo. `Invoke-OdooQualityChecks.ps1` verifica esas condiciones
consultando de nuevo el registro después de confirmarlo. El adaptador HTTP y las verificaciones
de contrato continúan siendo pruebas separadas.

## Seguridad y diagnóstico

- PostgreSQL no publica ningún puerto al host.
- La clave JSON-2 pertenece al usuario de integración y solo se carga en el backend.
- El Compose exige la contraseña de PostgreSQL y no contiene valores por defecto secretos.
- Ante un fallo, revisa `docker compose --env-file .env -f infra/odoo/docker-compose.yml logs --tail 100 odoo`.
- No adjuntes `.env`, encabezados HTTP, volcados de base ni logs sin sanitizar al Pull Request.

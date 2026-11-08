# Infraestructura local de Odoo

Esta configuración levanta Odoo 20 Community y PostgreSQL 16 en contenedores para desarrollo local. No configura TLS, copias de seguridad ni endurecimiento de producción.

## Requisitos

- Docker Desktop iniciado con contenedores Linux.
- Docker Compose v2.
- Archivo `.env` en la raíz del repositorio.

## Inicio en PowerShell

Desde la raíz del repositorio:

```powershell
Copy-Item .env.example .env
# Edita .env y reemplaza ODOO_POSTGRES_PASSWORD, Odoo__ApiKey y JWT_SECRET.
docker compose --env-file .env -f infra/odoo/docker-compose.yml up -d
docker compose --env-file .env -f infra/odoo/docker-compose.yml ps
```

Abre `http://localhost:8069`, crea la base configurada en `ODOO_DATABASE` y termina la configuración de Odoo. No reutilices la contraseña del ejemplo.

## Comprobación y apagado

```powershell
docker compose --env-file .env -f infra/odoo/docker-compose.yml logs --tail 100 odoo
docker compose --env-file .env -f infra/odoo/docker-compose.yml down
```

`down` conserva los volúmenes. Para borrar deliberadamente los datos locales de Odoo/PostgreSQL, se puede usar `down -v`.

Para cargar la configuración local en el proceso API desde PowerShell, ejecuta `infra/odoo/Load-LocalSettings.ps1` y luego inicia el host ASP.NET Core cuando se agregue al repositorio. Los valores reales solo se configuran localmente. La API key debe pertenecer al usuario de integración de Odoo y no es su contraseña de inicio de sesión.

## Estado de prueba real P09

Levantar los contenedores no demuestra que una venta se haya registrado. P09 solo podrá marcarse como superada después de registrar una venta de dos productos y pago en efectivo contra la instancia, verificando la respuesta de Odoo y sus identificadores en el mismo registro de prueba. El adaptador HTTP y las verificaciones de contrato no sustituyen este paso.

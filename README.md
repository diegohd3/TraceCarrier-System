# TraceCarrier System

API RESTful para trazabilidad industrial de `units` (piezas) y `carriers`, construida con .NET y preparada para PostgreSQL en Docker.

## About

Industrial traceability platform prototype with ASP.NET Core, PostgreSQL, Docker, and operator-focused dummy web stations for unit/carrier process flow control.

## Contenido

1. [Resumen](#resumen)
2. [Estado Actual](#estado-actual)
3. [Stack Tecnico](#stack-tecnico)
4. [Arquitectura y Organizacion](#arquitectura-y-organizacion)
5. [Flujo Funcional Implementado](#flujo-funcional-implementado)
6. [Endpoints REST](#endpoints-rest)
7. [Ejecucion Local](#ejecucion-local)
8. [PostgreSQL con Docker](#postgresql-con-docker)
9. [Esquema SQL](#esquema-sql)
10. [Estructura del Proyecto](#estructura-del-proyecto)
11. [Manejo de Errores HTTP](#manejo-de-errores-http)
12. [Siguientes Pasos Recomendados](#siguientes-pasos-recomendados)

## Resumen

Este proyecto expone una API para:

- Generar IDs de negocio irrepetibles para unidades y carriers.
- Crear unidades y carriers.
- Crear carrier y asociar lista de unidades en lote.
- Gestionar procesos de unidad y carrier con validacion de tiempo minimo.
- Bloquear avance al siguiente paso hasta la fecha permitida por proceso.
- Desvincular unidades de un carrier sin eliminar el carrier ni las unidades.

## Estado Actual

- La API compila y ejecuta correctamente.
- La logica de negocio usa persistencia directa a PostgreSQL mediante `Npgsql` en `TraceabilityService`.
- Docker + PostgreSQL + esquema SQL ya estan preparados para la siguiente fase de integracion.

## Stack Tecnico

- .NET `net10.0` (ASP.NET Core Web API)
- OpenAPI (`Microsoft.AspNetCore.OpenApi`)
- Swagger UI (`Swashbuckle.AspNetCore`)
- PostgreSQL 16 (Docker Compose)

## Arquitectura y Organizacion

Estructura por capas simples:

- `Controllers/`: endpoints HTTP y traduccion de errores.
- `Contracts/`: DTOs de request/response de la API.
- `Models/`: modelos de dominio.
- `Services/`: logica de negocio (`ITraceabilityService` + implementacion).

## Flujo Funcional Implementado

1. Generar `unit_id` irrepetible (`POST /api/units/id`).
2. Crear unidad (`POST /api/units`).
3. Iniciar proceso de unidad con tiempo minimo (`POST /api/units/{unitId}/processes`).
4. Completar proceso cuando cumpla tiempo minimo (`PATCH /api/units/{unitId}/processes/{processName}/complete`).
5. Generar y crear carrier (`POST /api/carriers/id`, `POST /api/carriers`).
6. Crear carrier con lista de unidades (`POST /api/carriers/assemble`).
7. Ejecutar procesos del carrier (inicio/completado).
8. Desvincular unidades del carrier sin eliminarlo (`POST /api/carriers/{carrierId}/finalization`).

## Control Temporal por Fecha

Cuando un proceso tiene `requiredTimeSeconds`, se calcula y guarda una fecha de habilitacion:

- En `units.next_process_available_at` para procesos de unidad.
- En `carriers.next_process_available_at` para procesos de carrier.
- En historial:
  - `process_history.ready_for_next_process_at`
  - `carrier_process_history.ready_for_next_process_at`

Validaciones aplicadas:

- `POST /api/carriers/assemble` y `PUT /api/carriers/{carrierId}/units/{unitId}`:
  valida que la unidad ya pueda pasar al siguiente paso.
- `POST /api/carriers/{carrierId}/finalization`:
  valida que el carrier ya pueda pasar al siguiente paso.

Si el tiempo no se cumple, la API responde `409 Conflict` con la fecha permitida.

## Endpoints REST

### Health

- `GET /`  
Devuelve estado basico del servicio.

### Units

- `POST /api/units/id`  
Genera `unit_id` irrepetible.

- `POST /api/units`  
Crea una unidad.

Request:

```json
{
  "unitId": "optional",
  "currentProcess": "process_1",
  "status": "created"
}
```

- `GET /api/units/{unitId}`  
Obtiene unidad por ID de negocio.

- `POST /api/units/{unitId}/processes`  
Inicia proceso de unidad.

Request:

```json
{
  "processName": "process_2",
  "requiredTimeSeconds": 10,
  "notes": "Second process started"
}
```

- `PATCH /api/units/{unitId}/processes/{processName}/complete`  
Completa proceso de unidad (valida tiempo minimo).

- `GET /api/units/{unitId}/processes`  
Obtiene historial de procesos de unidad.

### Carriers

- `POST /api/carriers/id`  
Genera `carrier_id` irrepetible.

- `POST /api/carriers`  
Crea un carrier.

Request:

```json
{
  "carrierId": "optional",
  "status": "active"
}
```

- `GET /api/carriers/{carrierId}`  
Obtiene carrier por ID de negocio.

- `PUT /api/carriers/{carrierId}/units/{unitId}`  
Asigna unidad a carrier.

- `POST /api/carriers/assemble`  
Crea carrier y asocia una lista de unidades en una sola operacion.

Request:

```json
{
  "carrierId": "optional",
  "carrierStatus": "loaded",
  "unitIds": ["UNIT-1001", "UNIT-1002"]
}
```

- `POST /api/carriers/{carrierId}/processes`  
Inicia proceso de carrier.

- `PATCH /api/carriers/{carrierId}/processes/{processName}/complete`  
Completa proceso de carrier (valida tiempo minimo).

- `GET /api/carriers/{carrierId}/processes`  
Obtiene historial de procesos de carrier.

- `POST /api/carriers/{carrierId}/finalization`  
Desvincula unidades seleccionadas del carrier, manteniendo el carrier en la base de datos.

Request:

```json
{
  "unitIdsToRelease": ["UNIT-REPLACE"],
  "releasedUnitStatus": "released_from_carrier",
  "releasedUnitProcess": "post_carrier_unlink"
}
```

## Ejecucion Local

### Requisitos

- .NET SDK compatible con `net10.0`
- Docker Desktop (para PostgreSQL)
- PowerShell (Windows)

### 1) Ejecutar API

Desde la raiz del repo (donde esta este `README.md`):

```powershell
dotnet restore "TraceCarrier System.slnx"
dotnet run --project ".\TraceCarrier System\TraceCarrier System.csproj"
```

URLs locales (`launchSettings.json`):

- HTTP: `http://localhost:5138`
- HTTPS: `https://localhost:7103`

Swagger UI en desarrollo:

- `http://localhost:5138/swagger`
- `https://localhost:7103/swagger`

### 2) Probar endpoints rapido

Usa:

- `TraceCarrier System/TraceCarrier System.http`
- Swagger UI
- Postman/Insomnia

### 3) Ejecutar app web dummy de operador

```powershell
dotnet run --project ".\TraceCarrier.OperatorDummy\TraceCarrier.OperatorDummy.csproj" --launch-profile http
```

URL:

- `http://localhost:5200`

Incluye 5 estaciones:

1. Crear unidad
2. Segundo proceso de unidad
3. Crear carrier y unir lista de unidades
4. Proceso de carrier
5. Desvincular unidades sin eliminar carrier

## PostgreSQL con Docker

### Archivos

- `docker-compose.yml`
- `.env.example`
- `init.sql`

### Levantar contenedor

```powershell
Copy-Item .env.example .env
docker compose up -d
docker compose ps
```

### Conectarse a PostgreSQL

```powershell
docker exec -it tracecarrier-postgres psql -U tracecarrier_app -d tracecarrier_db
```

### Verificar tablas creadas

```powershell
docker exec -it tracecarrier-postgres psql -U tracecarrier_app -d tracecarrier_db -c "\dt"
```

## Esquema SQL

`init.sql` crea:

- `units`
- `carriers`
- `carrier_units`
- `process_history`
- `carrier_process_history`

Columnas de control temporal:

- `units.next_process_available_at`
- `carriers.next_process_available_at`
- `process_history.ready_for_next_process_at`
- `carrier_process_history.ready_for_next_process_at`

Incluye:

- Restricciones PK/FK/UNIQUE
- Checks de integridad
- Indices de consulta
- Triggers de `updated_at`

## Estructura del Proyecto

```text
TraceCarrier System/
|- .env.example
|- .gitignore
|- docker-compose.yml
|- init.sql
|- TraceCarrier System.slnx
|- TraceCarrier.OperatorDummy/
|  |- Program.cs
|  |- appsettings.json
|  `- Pages/Stations/
`- TraceCarrier System/
   |- Program.cs
   |- TraceCarrier System.csproj
   |- TraceCarrier System.http
   |- appsettings.json
   |- appsettings.Development.json
   |- Contracts/
   |- Controllers/
   |- Models/
   |- Services/
   `- Properties/
```

## Manejo de Errores HTTP

En `TraceabilityController`:

- `ArgumentException` -> `400 Bad Request`
- `InvalidOperationException` -> `409 Conflict`
- `KeyNotFoundException` -> `404 Not Found`

## Siguientes Pasos Recomendados

1. Extraer capa de repositorios para separar SQL de reglas de negocio.
2. Evaluar migraciones con EF Core o Flyway para versionado de esquema.
3. Agregar pruebas:
   - Unitarias (servicios)
   - Integracion (endpoints + DB)
4. Agregar autenticacion/autorizacion para escenarios productivos.
5. Incorporar observabilidad:
   - logging estructurado
   - metricas y trazas

## Notas

- `.env` esta ignorado por `.gitignore` para proteger secretos locales.
- Si cambias `init.sql` y necesitas reinicializar la base local:

```powershell
docker compose down -v
docker compose up -d
```

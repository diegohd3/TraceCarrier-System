# TraceCarrier Flow Report

## Fecha de validacion
- 2026-03-25 (America/Mexico_City)

## Alcance implementado
- API REST .NET (`TraceCarrier System`) conectada a PostgreSQL en Docker.
- Persistencia real en base de datos para unidades, carriers, asignaciones e historiales.
- App web dummy de operador (`TraceCarrier.OperatorDummy`) con 5 estaciones del flujo.

## Flujo implementado (1 a 5)
1. Crear unidad y generar `unit_id` irrepetible.
2. Iniciar/completar segundo proceso con tiempo minimo requerido.
3. Crear carrier y asociar lista de unidades en una sola operacion.
4. Iniciar/completar procesos del carrier con tiempo minimo requerido.
5. Desvincular unidades del carrier sin eliminar el carrier.

## Control temporal agregado
- `units.next_process_available_at`: fecha desde la cual la unidad puede avanzar.
- `carriers.next_process_available_at`: fecha desde la cual el carrier puede avanzar.
- `process_history.ready_for_next_process_at`: fecha calculada por proceso de unidad.
- `carrier_process_history.ready_for_next_process_at`: fecha calculada por proceso de carrier.

Validaciones activas:
- Paso 3 (`carriers/assemble` y asignacion unitaria): bloquea si la unidad no cumple fecha.
- Paso 5 (`carriers/{carrierId}/finalization`): bloquea si el carrier no cumple fecha.

## Estaciones web dummy
- Dashboard: `/`
- Paso 1: `/Stations/Step1CreateUnit`
- Paso 2: `/Stations/Step2UnitSecondProcess`
- Paso 3: `/Stations/Step3AssembleCarrier`
- Paso 4: `/Stations/Step4CarrierProcess`
- Paso 5: `/Stations/Step5ReleaseUnits`

## Endpoints REST por proceso
- Paso 1
  - `POST /api/units/id`
  - `POST /api/units`
- Paso 2
  - `POST /api/units/{unitId}/processes`
  - `PATCH /api/units/{unitId}/processes/{processName}/complete`
- Paso 3
  - `POST /api/carriers/id`
  - `POST /api/carriers/assemble`
- Paso 4
  - `POST /api/carriers/{carrierId}/processes`
  - `PATCH /api/carriers/{carrierId}/processes/{processName}/complete`
- Paso 5
  - `POST /api/carriers/{carrierId}/finalization`

## Evidencia de prueba end-to-end
- Unit IDs creados:
  - `UNIT-9615A364EF71443B92A7046E6742C791`
  - `UNIT-515C367A08704CCBA7ADC37E97BC1B28`
- Carrier creado:
  - `CAR-52D9874197D24A3B9A50E68881E9FC83`
- Resultado de finalizacion:
  - `releasedUnitIds`: 2 unidades
  - `remainingAssignedUnitIds`: vacio
- Estado final:
  - Carrier se mantiene en BD con `status = unloaded`
  - Unidades quedan como:
    - `status = released_from_carrier`
    - `current_process = post_carrier_unlink`

## Evidencia de bloqueos por tiempo
- Intento temprano en Paso 3:
  - `HTTP 409`
  - Mensaje: unidad no puede avanzar aun, incluye fecha permitida y segundos restantes.
- Intento temprano en Paso 5:
  - `HTTP 409`
  - Mensaje: carrier no puede avanzar aun, incluye fecha permitida y segundos restantes.

## Validacion SQL posterior
- `units_count = 2`
- `carriers_count = 1`
- `carrier_units_count = 0`
- `unit_process_history_count = 2`
- `carrier_process_history_count = 1`

## Estado de ejecucion local
- PostgreSQL Docker: `tracecarrier-postgres` activo y healthy.
- API REST: responde en `http://localhost:5138`.
- Operator Dummy: responde en `http://localhost:5200`.

## Comandos usados para levantar el entorno (Windows)
```powershell
docker compose up -d
dotnet run --project "TraceCarrier System/TraceCarrier System.csproj" --launch-profile http
dotnet run --project "TraceCarrier.OperatorDummy/TraceCarrier.OperatorDummy.csproj" --launch-profile http
```

using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;
using TraceCarrier_System.Contracts;
using TraceCarrier_System.Models;
using TraceCarrier_System.Services;

namespace TraceCarrier_System.Controllers;

[ApiController]
[Route("api")]
public sealed class TraceabilityController : ControllerBase
{
    private readonly ITraceabilityService _service;

    public TraceabilityController(ITraceabilityService service)
    {
        _service = service;
    }

    [HttpPost("units/id")]
    [SwaggerOperation(
        Summary = "Generate unit business ID",
        Description = "Generates a new non-repeating unit_id for traceability workflows.")]
    public ActionResult<GenerateUnitIdResponse> GenerateUnitId()
    {
        return Ok(new GenerateUnitIdResponse
        {
            UnitId = _service.GenerateUnitId(),
            GeneratedAt = DateTimeOffset.UtcNow
        });
    }

    [HttpPost("units")]
    [SwaggerOperation(
        Summary = "Create unit",
        Description = "Creates a new unit with its initial process and status.")]
    public ActionResult<Unit> CreateUnit([FromBody] CreateUnitRequest request)
    {
        return HandleResult<Unit>(
            () =>
            {
                var unit = _service.CreateUnit(request);
                return CreatedAtAction(nameof(GetUnit), new { unitId = unit.UnitId }, unit);
            });
    }

    [HttpGet("units/{unitId}")]
    [SwaggerOperation(
        Summary = "Get unit by ID",
        Description = "Returns a unit using its business identifier (unit_id).")]
    public ActionResult<Unit> GetUnit(string unitId)
    {
        return HandleResult<Unit>(
            () =>
            {
                var unit = _service.GetUnitByBusinessId(unitId);
                return unit is null ? NotFound() : Ok(unit);
            });
    }

    [HttpPost("units/{unitId}/processes")]
    [SwaggerOperation(
        Summary = "Start unit process",
        Description = "Starts a new process step for a unit and registers required minimum time.")]
    public ActionResult<ProcessHistory> StartUnitProcess(string unitId, [FromBody] StartProcessRequest request)
    {
        return HandleResult<ProcessHistory>(
            () =>
            {
                var process = _service.StartUnitProcess(unitId, request);
                return Ok(process);
            });
    }

    [HttpPatch("units/{unitId}/processes/{processName}/complete")]
    [SwaggerOperation(
        Summary = "Complete unit process",
        Description = "Completes the active process for a unit when minimum required time is met.")]
    public ActionResult<ProcessHistory> CompleteUnitProcess(string unitId, string processName)
    {
        return HandleResult<ProcessHistory>(
            () =>
            {
                var process = _service.CompleteUnitProcess(unitId, processName);
                return Ok(process);
            });
    }

    [HttpGet("units/{unitId}/processes")]
    [SwaggerOperation(
        Summary = "Get unit process history",
        Description = "Returns all process records associated with a unit.")]
    public ActionResult<IReadOnlyCollection<ProcessHistory>> GetUnitProcessHistory(string unitId)
    {
        return HandleResult<IReadOnlyCollection<ProcessHistory>>(
            () =>
            {
                var history = _service.GetUnitProcessHistory(unitId);
                return Ok(history);
            });
    }

    [HttpPost("carriers/id")]
    [SwaggerOperation(
        Summary = "Generate carrier business ID",
        Description = "Generates a new non-repeating carrier_id for traceability workflows.")]
    public ActionResult<GenerateCarrierIdResponse> GenerateCarrierId()
    {
        return Ok(new GenerateCarrierIdResponse
        {
            CarrierId = _service.GenerateCarrierId(),
            GeneratedAt = DateTimeOffset.UtcNow
        });
    }

    [HttpPost("carriers")]
    [SwaggerOperation(
        Summary = "Create carrier",
        Description = "Creates a new carrier with initial status.")]
    public ActionResult<Carrier> CreateCarrier([FromBody] CreateCarrierRequest request)
    {
        return HandleResult<Carrier>(
            () =>
            {
                var carrier = _service.CreateCarrier(request);
                return CreatedAtAction(nameof(GetCarrier), new { carrierId = carrier.CarrierId }, carrier);
            });
    }

    [HttpPost("carriers/assemble")]
    [SwaggerOperation(
        Summary = "Create carrier from unit list",
        Description = "Creates a carrier and assigns a list of units in one operation.")]
    public ActionResult<CreateCarrierFromUnitsResult> CreateCarrierFromUnits([FromBody] CreateCarrierFromUnitsRequest request)
    {
        return HandleResult<CreateCarrierFromUnitsResult>(
            () =>
            {
                var result = _service.CreateCarrierFromUnits(request);
                return CreatedAtAction(nameof(GetCarrier), new { carrierId = result.Carrier.CarrierId }, result);
            });
    }

    [HttpGet("carriers/{carrierId}")]
    [SwaggerOperation(
        Summary = "Get carrier by ID",
        Description = "Returns a carrier using its business identifier (carrier_id).")]
    public ActionResult<Carrier> GetCarrier(string carrierId)
    {
        return HandleResult<Carrier>(
            () =>
            {
                var carrier = _service.GetCarrierByBusinessId(carrierId);
                return carrier is null ? NotFound() : Ok(carrier);
            });
    }

    [HttpPut("carriers/{carrierId}/units/{unitId}")]
    [SwaggerOperation(
        Summary = "Assign unit to carrier",
        Description = "Assigns one unit to a carrier. A unit can only be assigned to one carrier at a time.")]
    public ActionResult<CarrierUnit> AssignUnitToCarrier(string carrierId, string unitId)
    {
        return HandleResult<CarrierUnit>(
            () =>
            {
                var assignment = _service.AssignUnitToCarrier(carrierId, unitId);
                return Ok(assignment);
            });
    }

    [HttpPost("carriers/{carrierId}/processes")]
    [SwaggerOperation(
        Summary = "Start carrier process",
        Description = "Starts a process step for a carrier and registers required minimum time.")]
    public ActionResult<CarrierProcessHistory> StartCarrierProcess(string carrierId, [FromBody] StartProcessRequest request)
    {
        return HandleResult<CarrierProcessHistory>(
            () =>
            {
                var process = _service.StartCarrierProcess(carrierId, request);
                return Ok(process);
            });
    }

    [HttpPatch("carriers/{carrierId}/processes/{processName}/complete")]
    [SwaggerOperation(
        Summary = "Complete carrier process",
        Description = "Completes the active process for a carrier when minimum required time is met.")]
    public ActionResult<CarrierProcessHistory> CompleteCarrierProcess(string carrierId, string processName)
    {
        return HandleResult<CarrierProcessHistory>(
            () =>
            {
                var process = _service.CompleteCarrierProcess(carrierId, processName);
                return Ok(process);
            });
    }

    [HttpGet("carriers/{carrierId}/processes")]
    [SwaggerOperation(
        Summary = "Get carrier process history",
        Description = "Returns all process records associated with a carrier.")]
    public ActionResult<IReadOnlyCollection<CarrierProcessHistory>> GetCarrierProcessHistory(string carrierId)
    {
        return HandleResult<IReadOnlyCollection<CarrierProcessHistory>>(
            () =>
            {
                var history = _service.GetCarrierProcessHistory(carrierId);
                return Ok(history);
            });
    }

    [HttpPost("carriers/{carrierId}/finalization")]
    [SwaggerOperation(
        Summary = "Finalize carrier",
        Description = "Unlinks selected units from a carrier without deleting the carrier.")]
    public ActionResult<FinalizeCarrierResult> FinalizeCarrier(string carrierId, [FromBody] FinalizeCarrierRequest request)
    {
        return HandleResult<FinalizeCarrierResult>(
            () =>
            {
                var result = _service.FinalizeCarrier(
                    carrierId,
                    request.UnitIdsToRelease,
                    request.ReleasedUnitStatus,
                    request.ReleasedUnitProcess);
                return Ok(result);
            });
    }

    private ActionResult<T> HandleResult<T>(Func<ActionResult<T>> action)
    {
        try
        {
            return action();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }
}

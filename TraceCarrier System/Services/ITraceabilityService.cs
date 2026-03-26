using TraceCarrier_System.Contracts;
using TraceCarrier_System.Models;

namespace TraceCarrier_System.Services;

public interface ITraceabilityService
{
    string GenerateUnitId();

    string GenerateCarrierId();

    Unit CreateUnit(CreateUnitRequest request);

    Carrier CreateCarrier(CreateCarrierRequest request);

    CreateCarrierFromUnitsResult CreateCarrierFromUnits(CreateCarrierFromUnitsRequest request);

    CarrierUnit AssignUnitToCarrier(string carrierId, string unitId);

    ProcessHistory StartUnitProcess(string unitId, StartProcessRequest request);

    ProcessHistory CompleteUnitProcess(string unitId, string processName);

    Unit? GetUnitByBusinessId(string unitId);

    Carrier? GetCarrierByBusinessId(string carrierId);

    IReadOnlyCollection<ProcessHistory> GetUnitProcessHistory(string unitId);

    CarrierProcessHistory StartCarrierProcess(string carrierId, StartProcessRequest request);

    CarrierProcessHistory CompleteCarrierProcess(string carrierId, string processName);

    IReadOnlyCollection<CarrierProcessHistory> GetCarrierProcessHistory(string carrierId);

    FinalizeCarrierResult FinalizeCarrier(
        string carrierId,
        IReadOnlyCollection<string> unitIdsToRelease,
        string releasedUnitStatus,
        string releasedUnitProcess);
}

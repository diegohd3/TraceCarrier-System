using TraceCarrier_System.Models;

namespace TraceCarrier_System.Contracts;

public sealed class CreateCarrierFromUnitsResult
{
    public required Carrier Carrier { get; init; }

    public required IReadOnlyCollection<string> AssignedUnitIds { get; init; }
}

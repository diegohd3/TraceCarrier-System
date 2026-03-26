namespace TraceCarrier_System.Contracts;

public sealed class FinalizeCarrierResult
{
    public required string CarrierId { get; init; }

    public required IReadOnlyCollection<string> ReleasedUnitIds { get; init; }

    public required IReadOnlyCollection<string> RemainingAssignedUnitIds { get; init; }
}

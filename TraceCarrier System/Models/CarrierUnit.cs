namespace TraceCarrier_System.Models;

public sealed class CarrierUnit
{
    public long Id { get; init; }

    public long CarrierId { get; init; }

    public long UnitId { get; init; }

    public DateTimeOffset AssignedAt { get; init; }
}

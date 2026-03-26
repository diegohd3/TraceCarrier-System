namespace TraceCarrier_System.Models;

public sealed class Unit
{
    public long Id { get; init; }

    public required string UnitId { get; init; }

    public required string CurrentProcess { get; set; }

    public required string Status { get; set; }

    public DateTimeOffset? NextProcessAvailableAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}

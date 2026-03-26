namespace TraceCarrier_System.Models;

public sealed class CarrierProcessHistory
{
    public long Id { get; init; }

    public long CarrierId { get; init; }

    public required string ProcessName { get; init; }

    public DateTimeOffset StartTime { get; init; }

    public DateTimeOffset? EndTime { get; set; }

    public int RequiredTimeSeconds { get; init; }

    public DateTimeOffset ReadyForNextProcessAt { get; init; }

    public bool Completed { get; set; }

    public string? Notes { get; init; }
}

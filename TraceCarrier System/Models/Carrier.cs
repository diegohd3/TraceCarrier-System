namespace TraceCarrier_System.Models;

public sealed class Carrier
{
    public long Id { get; init; }

    public required string CarrierId { get; init; }

    public required string Status { get; set; }

    public DateTimeOffset? NextProcessAvailableAt { get; set; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; set; }
}

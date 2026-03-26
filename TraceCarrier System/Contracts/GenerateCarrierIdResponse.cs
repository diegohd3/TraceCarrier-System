namespace TraceCarrier_System.Contracts;

public sealed class GenerateCarrierIdResponse
{
    public required string CarrierId { get; init; }

    public DateTimeOffset GeneratedAt { get; init; }
}

namespace TraceCarrier_System.Contracts;

public sealed class GenerateUnitIdResponse
{
    public required string UnitId { get; init; }

    public DateTimeOffset GeneratedAt { get; init; }
}

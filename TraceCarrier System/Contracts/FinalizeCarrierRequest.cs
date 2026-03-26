using System.ComponentModel.DataAnnotations;

namespace TraceCarrier_System.Contracts;

public sealed class FinalizeCarrierRequest
{
    public IReadOnlyCollection<string> UnitIdsToRelease { get; init; } = Array.Empty<string>();

    [MaxLength(40)]
    public string ReleasedUnitStatus { get; init; } = "released_from_carrier";

    [MaxLength(100)]
    public string ReleasedUnitProcess { get; init; } = "post_carrier_unlink";
}

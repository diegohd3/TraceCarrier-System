using System.ComponentModel.DataAnnotations;

namespace TraceCarrier_System.Contracts;

public sealed class CreateCarrierFromUnitsRequest
{
    [MaxLength(64)]
    public string? CarrierId { get; init; }

    [Required]
    [MaxLength(40)]
    public string CarrierStatus { get; init; } = "loaded";

    [Required]
    [MinLength(1)]
    public IReadOnlyCollection<string> UnitIds { get; init; } = Array.Empty<string>();
}

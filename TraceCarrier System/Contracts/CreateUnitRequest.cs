using System.ComponentModel.DataAnnotations;

namespace TraceCarrier_System.Contracts;

public sealed class CreateUnitRequest
{
    [MaxLength(64)]
    public string? UnitId { get; init; }

    [Required]
    [MaxLength(100)]
    public string CurrentProcess { get; init; } = string.Empty;

    [Required]
    [MaxLength(40)]
    public string Status { get; init; } = "created";
}

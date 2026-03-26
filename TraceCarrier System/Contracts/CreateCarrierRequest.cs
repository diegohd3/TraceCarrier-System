using System.ComponentModel.DataAnnotations;

namespace TraceCarrier_System.Contracts;

public sealed class CreateCarrierRequest
{
    [MaxLength(64)]
    public string? CarrierId { get; init; }

    [Required]
    [MaxLength(40)]
    public string Status { get; init; } = "active";
}

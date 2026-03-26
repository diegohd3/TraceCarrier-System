using System.ComponentModel.DataAnnotations;

namespace TraceCarrier_System.Contracts;

public sealed class StartProcessRequest
{
    [Required]
    [MaxLength(100)]
    public string ProcessName { get; init; } = string.Empty;

    [Range(0, int.MaxValue)]
    public int RequiredTimeSeconds { get; init; }

    [MaxLength(1000)]
    public string? Notes { get; init; }
}

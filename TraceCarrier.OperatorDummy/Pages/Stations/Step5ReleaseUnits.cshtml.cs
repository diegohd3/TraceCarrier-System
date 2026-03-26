using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TraceCarrier.OperatorDummy.Services;

namespace TraceCarrier.OperatorDummy.Pages.Stations;

public sealed class Step5ReleaseUnitsModel : PageModel
{
    private readonly OperatorApiClient _apiClient;

    public Step5ReleaseUnitsModel(OperatorApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty]
    public Step5ReleaseUnitsInput Input { get; set; } = new();

    public string Message { get; private set; } = string.Empty;

    public string ApiResponse { get; private set; } = string.Empty;

    public bool Success { get; private set; }

    public async Task<IActionResult> OnPostReleaseAsync()
    {
        var body = new
        {
            unitIdsToRelease = ParseUnitIds(Input.UnitIdsToReleaseRaw),
            releasedUnitStatus = Input.ReleasedUnitStatus,
            releasedUnitProcess = Input.ReleasedUnitProcess
        };

        var result = await _apiClient.SendAsync(
            HttpMethod.Post,
            $"/api/carriers/{Input.CarrierId}/finalization",
            body);

        ApiResponse = result.Payload;
        Success = result.IsSuccess;
        Message = result.IsSuccess
            ? "Unidades desvinculadas del carrier."
            : $"No se pudo desvincular unidades (HTTP {result.StatusCode}).";
        return Page();
    }

    private static IReadOnlyCollection<string> ParseUnitIds(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw
            .Split([',', ';', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

public sealed class Step5ReleaseUnitsInput
{
    public string CarrierId { get; set; } = string.Empty;

    public string UnitIdsToReleaseRaw { get; set; } = string.Empty;

    public string ReleasedUnitStatus { get; set; } = "released_from_carrier";

    public string ReleasedUnitProcess { get; set; } = "post_carrier_unlink";
}

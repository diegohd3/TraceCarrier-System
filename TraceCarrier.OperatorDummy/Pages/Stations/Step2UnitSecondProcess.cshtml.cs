using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TraceCarrier.OperatorDummy.Services;

namespace TraceCarrier.OperatorDummy.Pages.Stations;

public sealed class Step2UnitSecondProcessModel : PageModel
{
    private readonly OperatorApiClient _apiClient;

    public Step2UnitSecondProcessModel(OperatorApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty]
    public Step2UnitProcessInput Input { get; set; } = new();

    public string Message { get; private set; } = string.Empty;

    public string ApiResponse { get; private set; } = string.Empty;

    public bool Success { get; private set; }

    public async Task<IActionResult> OnPostStartAsync()
    {
        var body = new
        {
            processName = Input.ProcessName,
            requiredTimeSeconds = Input.RequiredTimeSeconds,
            notes = Input.Notes
        };

        var result = await _apiClient.SendAsync(
            HttpMethod.Post,
            $"/api/units/{Input.UnitId}/processes",
            body);

        ApiResponse = result.Payload;
        Success = result.IsSuccess;
        Message = result.IsSuccess
            ? "Proceso de unidad iniciado."
            : $"No se pudo iniciar el proceso (HTTP {result.StatusCode}).";
        return Page();
    }

    public async Task<IActionResult> OnPostCompleteAsync()
    {
        var result = await _apiClient.SendAsync(
            HttpMethod.Patch,
            $"/api/units/{Input.UnitId}/processes/{Input.ProcessName}/complete");

        ApiResponse = result.Payload;
        Success = result.IsSuccess;
        Message = result.IsSuccess
            ? "Proceso de unidad completado."
            : $"No se pudo completar el proceso (HTTP {result.StatusCode}).";
        return Page();
    }
}

public sealed class Step2UnitProcessInput
{
    public string UnitId { get; set; } = string.Empty;

    public string ProcessName { get; set; } = "process_2";

    public int RequiredTimeSeconds { get; set; } = 10;

    public string? Notes { get; set; }
}

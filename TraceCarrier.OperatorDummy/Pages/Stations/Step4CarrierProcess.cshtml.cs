using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TraceCarrier.OperatorDummy.Services;

namespace TraceCarrier.OperatorDummy.Pages.Stations;

public sealed class Step4CarrierProcessModel : PageModel
{
    private readonly OperatorApiClient _apiClient;

    public Step4CarrierProcessModel(OperatorApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty]
    public Step4CarrierProcessInput Input { get; set; } = new();

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
            $"/api/carriers/{Input.CarrierId}/processes",
            body);

        ApiResponse = result.Payload;
        Success = result.IsSuccess;
        Message = result.IsSuccess
            ? "Proceso de carrier iniciado."
            : $"No se pudo iniciar proceso de carrier (HTTP {result.StatusCode}).";
        return Page();
    }

    public async Task<IActionResult> OnPostCompleteAsync()
    {
        var result = await _apiClient.SendAsync(
            HttpMethod.Patch,
            $"/api/carriers/{Input.CarrierId}/processes/{Input.ProcessName}/complete");

        ApiResponse = result.Payload;
        Success = result.IsSuccess;
        Message = result.IsSuccess
            ? "Proceso de carrier completado."
            : $"No se pudo completar proceso de carrier (HTTP {result.StatusCode}).";
        return Page();
    }
}

public sealed class Step4CarrierProcessInput
{
    public string CarrierId { get; set; } = string.Empty;

    public string ProcessName { get; set; } = "carrier_process_1";

    public int RequiredTimeSeconds { get; set; } = 20;

    public string? Notes { get; set; }
}

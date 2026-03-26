using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TraceCarrier.OperatorDummy.Services;

namespace TraceCarrier.OperatorDummy.Pages.Stations;

public sealed class Step1CreateUnitModel : PageModel
{
    private readonly OperatorApiClient _apiClient;

    public Step1CreateUnitModel(OperatorApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty]
    public Step1CreateUnitInput Input { get; set; } = new();

    public string Message { get; private set; } = string.Empty;

    public string ApiResponse { get; private set; } = string.Empty;

    public bool Success { get; private set; }

    public async Task<IActionResult> OnPostGenerateIdAsync()
    {
        var result = await _apiClient.SendAsync(HttpMethod.Post, "/api/units/id");
        ApiResponse = result.Payload;
        Success = result.IsSuccess;
        Message = result.IsSuccess
            ? "Unit ID generado correctamente."
            : $"Error al generar Unit ID (HTTP {result.StatusCode}).";
        return Page();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        var body = new
        {
            unitId = string.IsNullOrWhiteSpace(Input.UnitId) ? null : Input.UnitId,
            currentProcess = Input.CurrentProcess,
            status = Input.Status
        };

        var result = await _apiClient.SendAsync(HttpMethod.Post, "/api/units", body);
        ApiResponse = result.Payload;
        Success = result.IsSuccess;
        Message = result.IsSuccess
            ? "Unidad creada en la base de datos."
            : $"No se pudo crear la unidad (HTTP {result.StatusCode}).";
        return Page();
    }
}

public sealed class Step1CreateUnitInput
{
    public string? UnitId { get; set; }

    public string CurrentProcess { get; set; } = "process_1";

    public string Status { get; set; } = "created";
}

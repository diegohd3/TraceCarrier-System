using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using TraceCarrier.OperatorDummy.Services;

namespace TraceCarrier.OperatorDummy.Pages.Stations;

public sealed class Step3AssembleCarrierModel : PageModel
{
    private readonly OperatorApiClient _apiClient;

    public Step3AssembleCarrierModel(OperatorApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [BindProperty]
    public Step3AssembleCarrierInput Input { get; set; } = new();

    public string Message { get; private set; } = string.Empty;

    public string ApiResponse { get; private set; } = string.Empty;

    public bool Success { get; private set; }

    public async Task<IActionResult> OnPostGenerateCarrierIdAsync()
    {
        var result = await _apiClient.SendAsync(HttpMethod.Post, "/api/carriers/id");
        ApiResponse = result.Payload;
        Success = result.IsSuccess;
        Message = result.IsSuccess
            ? "Carrier ID generado correctamente."
            : $"Error al generar Carrier ID (HTTP {result.StatusCode}).";
        return Page();
    }

    public async Task<IActionResult> OnPostAssembleAsync()
    {
        var unitIds = ParseUnitIds(Input.UnitIdsRaw);
        var body = new
        {
            carrierId = string.IsNullOrWhiteSpace(Input.CarrierId) ? null : Input.CarrierId,
            carrierStatus = Input.CarrierStatus,
            unitIds
        };

        var result = await _apiClient.SendAsync(HttpMethod.Post, "/api/carriers/assemble", body);
        ApiResponse = result.Payload;
        Success = result.IsSuccess;
        Message = result.IsSuccess
            ? "Carrier creado y unidades asociadas."
            : $"No se pudo ensamblar carrier (HTTP {result.StatusCode}).";
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

public sealed class Step3AssembleCarrierInput
{
    public string? CarrierId { get; set; }

    public string CarrierStatus { get; set; } = "loaded";

    public string UnitIdsRaw { get; set; } = string.Empty;
}

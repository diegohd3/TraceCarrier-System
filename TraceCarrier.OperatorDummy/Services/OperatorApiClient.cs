using System.Net.Http.Json;
using System.Text.Json;

namespace TraceCarrier.OperatorDummy.Services;

public sealed class OperatorApiClient
{
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public OperatorApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<ApiCallResult> SendAsync(HttpMethod method, string relativePath, object? body = null)
    {
        using var request = new HttpRequestMessage(method, relativePath);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        using var response = await _httpClient.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();
        var formatted = TryFormatJson(payload);

        return new ApiCallResult
        {
            IsSuccess = response.IsSuccessStatusCode,
            StatusCode = (int)response.StatusCode,
            Payload = formatted
        };
    }

    private static string TryFormatJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "(empty response)";
        }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            return JsonSerializer.Serialize(doc, JsonOptions);
        }
        catch
        {
            return raw;
        }
    }
}

public sealed class ApiCallResult
{
    public bool IsSuccess { get; init; }

    public int StatusCode { get; init; }

    public string Payload { get; init; } = string.Empty;
}

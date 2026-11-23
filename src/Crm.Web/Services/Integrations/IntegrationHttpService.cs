using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Crm.Data.Entities;

namespace Crm.Web.Services.Integrations;

public class IntegrationRequestResult
{
    public int StatusCode { get; set; }
    public long DurationMs { get; set; }
    public string? BodyPreview { get; set; }
    public string? ContentType { get; set; }
    public string? Error { get; set; }
    public bool Success => Error == null && StatusCode >= 200 && StatusCode < 300;
    public JsonElement? Json { get; set; }
}

/// <summary>Builds and executes outbound requests for an integration config (with SSRF guard).</summary>
public class IntegrationHttpService
{
    private readonly IHttpClientFactory _httpFactory;

    public IntegrationHttpService(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    public static string BuildUrl(IntegrationConfig cfg)
    {
        var baseUrl = cfg.BaseUrl.TrimEnd('/');
        var endpoint = cfg.Endpoint.TrimStart('/');
        var url = string.IsNullOrEmpty(endpoint) ? baseUrl : $"{baseUrl}/{endpoint}";

        var query = ParseJsonArray(cfg.QueryParamsJson);
        if (query.Count > 0)
        {
            var sep = url.Contains('?') ? "&" : "?";
            url += sep + string.Join("&", query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value ?? "")}"));
        }
        return url;
    }

    /// <summary>Send the configured request (used by Test Connection and Sync).</summary>
    public async Task<IntegrationRequestResult> SendAsync(IntegrationConfig cfg, string? decryptedApiKey = null,
        string? decryptedBearer = null, string? decryptedUsername = null, string? decryptedPassword = null,
        string? decryptedCustomHeaderValue = null, string? requestBodyOverride = null,
        CancellationToken ct = default)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new IntegrationRequestResult();

        try
        {
            var url = await OutboundUrlGuard.ValidateAsync(BuildUrl(cfg), ct);
            var client = _httpFactory.CreateClient("integrations");
            client.Timeout = TimeSpan.FromSeconds(Math.Clamp(cfg.TimeoutSeconds, 5, 120));

            using var request = new HttpRequestMessage(new HttpMethod(cfg.HttpMethod.ToUpperInvariant()), url);

            // Auth
            switch (cfg.AuthType)
            {
                case IntegrationAuthType.ApiKey when decryptedApiKey != null:
                    request.Headers.Add("X-API-Key", decryptedApiKey);
                    break;
                case IntegrationAuthType.BearerToken when decryptedBearer != null:
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", decryptedBearer);
                    break;
                case IntegrationAuthType.BasicAuth when decryptedUsername != null:
                    var creds = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{decryptedUsername}:{decryptedPassword}"));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", creds);
                    break;
                case IntegrationAuthType.CustomHeader when !string.IsNullOrEmpty(cfg.CustomHeaderName):
                    request.Headers.TryAddWithoutValidation(cfg.CustomHeaderName, decryptedCustomHeaderValue ?? "");
                    break;
                case IntegrationAuthType.OAuth2 when decryptedBearer != null:
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", decryptedBearer);
                    break;
            }

            // Custom headers
            foreach (var kv in ParseJsonArray(cfg.CustomHeadersJson))
                request.Headers.TryAddWithoutValidation(kv.Key, kv.Value ?? "");

            // Body
            var body = requestBodyOverride ?? cfg.RequestBody;
            if (!string.IsNullOrWhiteSpace(body) && cfg.HttpMethod.ToUpperInvariant() is "POST" or "PUT" or "PATCH")
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            result.StatusCode = (int)response.StatusCode;
            result.ContentType = response.Content.Headers.ContentType?.ToString();

            var text = await response.Content.ReadAsStringAsync(ct);
            result.BodyPreview = text.Length > 8000 ? text[..8000] + "…[truncated]" : text;

            if (response.Content.Headers.ContentType?.MediaType?.Contains("json") == true ||
                (!string.IsNullOrWhiteSpace(text) && (text.TrimStart().StartsWith('{') || text.TrimStart().StartsWith('['))))
            {
                try { result.Json = JsonDocument.Parse(text).RootElement.Clone(); } catch { /* not json */ }
            }

            if (!result.Success && result.Error == null)
                result.Error = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
        }
        catch (UnsafeUrlException ex)
        {
            result.Error = $"Blocked: {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            result.Error = $"Request timed out after {cfg.TimeoutSeconds}s.";
        }
        catch (HttpRequestException ex)
        {
            result.Error = $"Connection failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            result.Error = $"Error: {ex.Message}";
        }
        finally
        {
            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
        }
        return result;
    }

    public static List<KeyValuePair<string, string?>> ParseJsonArray(string? json)
    {
        var list = new List<KeyValuePair<string, string?>>();
        if (string.IsNullOrWhiteSpace(json)) return list;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in doc.RootElement.EnumerateArray())
                {
                    var name = el.TryGetProperty("name", out var n) ? n.GetString()
                        : el.TryGetProperty("key", out var k) ? k.GetString() : null;
                    var value = el.TryGetProperty("value", out var v) ? v.GetString() : null;
                    if (!string.IsNullOrEmpty(name)) list.Add(new(name, value));
                }
            }
        }
        catch { /* malformed config JSON */ }
        return list;
    }
}

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Crm.Data.Entities;
using Microsoft.Extensions.Caching.Memory;

namespace Crm.Web.Services.Ai;

/// <summary>
/// Generates text embeddings via the configured AI provider.
/// Supports OpenAI-compatible /embeddings endpoints (OpenAI, Azure-compat, vLLM, Ollama)
/// and Gemini embedContent. Returns null when unavailable — callers fall back to keyword search.
/// </summary>
public class EmbeddingService
{
    private readonly IHttpClientFactory _http;
    private readonly IMemoryCache _cache;
    private const string CachePrefix = "emb:";

    public EmbeddingService(IHttpClientFactory http, IMemoryCache cache)
    { _http = http; _cache = cache; }

    public bool CanEmbed(AiProviderConfig p) =>
        p.ProviderType is AiProviderType.OpenAI or AiProviderType.OpenAICompatible
        || !string.IsNullOrWhiteSpace(p.Endpoint);

    public async Task<float[]?> EmbedAsync(AiProviderConfig p, string apiKey, string text, CancellationToken ct = default)
    {
        var cacheKey = CachePrefix + p.Id + ":" + text.GetHashCode();
        if (_cache.TryGetValue(cacheKey, out float[]? cached)) return cached;

        var endpoint = ResolveEndpoint(p);
        object payload; HttpRequestMessage req;

        if (p.ProviderType == AiProviderType.Gemini)
        {
            payload = new { model = p.Model, content = new { parts = new[] { new { text } } } };
            req = new HttpRequestMessage(HttpMethod.Post, endpoint)
            { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
        }
        else
        {
            // OpenAI-compatible embeddings
            var url = endpoint.EndsWith("/embeddings") ? endpoint : endpoint.TrimEnd('/') + "/embeddings";
            payload = new { model = p.Model, input = text };
            req = new HttpRequestMessage(HttpMethod.Post, url)
            { Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json") };
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        try
        {
            var client = _http.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            if (p.ProviderType == AiProviderType.Gemini)
                req.RequestUri = new Uri(req.RequestUri!.ToString() + "?key=" + apiKey);
            else if (p.ProviderType == AiProviderType.Gemini)
                req.Headers.Add("x-goog-api-key", apiKey);

            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;
            var body = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            float[] vec = p.ProviderType == AiProviderType.Gemini
                ? doc.RootElement.GetProperty("embedding").GetProperty("values").EnumerateArray().Select(e => (float)e.GetDouble()).ToArray()
                : doc.RootElement.GetProperty("data")[0].GetProperty("embedding").EnumerateArray().Select(e => (float)e.GetDouble()).ToArray();
            _cache.Set(cacheKey, vec, TimeSpan.FromHours(1));
            return vec;
        }
        catch { return null; }
    }

    private static string ResolveEndpoint(AiProviderConfig p) =>
        !string.IsNullOrWhiteSpace(p.Endpoint) ? p.Endpoint :
        p.ProviderType == AiProviderType.Gemini
            ? $"https://generativelanguage.googleapis.com/v1beta/models/{p.Model}:embedContent"
            : "https://api.openai.com/v1/embeddings";

    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    public static float[]? ParseVector(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray().Select(e => (float)e.GetDouble()).ToArray();
        }
        catch { return null; }
    }
}

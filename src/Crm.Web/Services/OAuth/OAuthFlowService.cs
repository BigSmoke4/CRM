using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Crm.Data.Entities;
using Crm.Web.Services;

namespace Crm.Web.Services.OAuth;

/// <summary>Generic OAuth2 authorization-code flow for Gmail/Google, M365, Slack, or custom providers.</summary>
public class OAuthProvider
{
    public string Key { get; init; } = "";
    public string Name { get; init; } = "";
    public string AuthorizeUrl { get; init; } = "";
    public string TokenUrl { get; init; } = "";
    public string DefaultScope { get; init; } = "";
}

public static class OAuthProviders
{
    public static readonly OAuthProvider[] All =
    {
        new() { Key="gmail", Name="Gmail / Google", AuthorizeUrl="https://accounts.google.com/o/oauth2/v2/auth",
            TokenUrl="https://oauth2.googleapis.com/token", DefaultScope="https://www.googleapis.com/auth/gmail.send https://www.googleapis.com/auth/calendar" },
        new() { Key="m365", Name="Microsoft 365", AuthorizeUrl="https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
            TokenUrl="https://login.microsoftonline.com/common/oauth2/v2.0/token", DefaultScope="https://graph.microsoft.com/Mail.Send https://graph.microsoft.com/Calendars.ReadWrite offline_access" },
        new() { Key="slack", Name="Slack", AuthorizeUrl="https://slack.com/oauth/v2/authorize",
            TokenUrl="https://slack.com/api/oauth.v2.access", DefaultScope="chat:write channels:read" },
        new() { Key="custom", Name="Custom OAuth2", AuthorizeUrl="", TokenUrl="", DefaultScope="" }
    };

    public static OAuthProvider? Find(string key) => All.FirstOrDefault(p => p.Key == key);
}

public class OAuthFlowService
{
    private readonly IHttpClientFactory _http;
    private readonly ISecretProtector _secrets;

    public OAuthFlowService(IHttpClientFactory http, ISecretProtector secrets)
    { _http = http; _secrets = secrets; }

    /// <summary>Build the provider authorize URL the user's browser is redirected to.</summary>
    public string BuildAuthorizeUrl(OAuthProvider provider, string clientId, string redirectUri, string? scope, string? state)
    {
        var sep = provider.AuthorizeUrl.Contains('?') ? "&" : "?";
        return $"{provider.AuthorizeUrl}{sep}" +
            $"client_id={Uri.EscapeDataString(clientId)}" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&response_type=code" +
            $"&scope={Uri.EscapeDataString(scope ?? provider.DefaultScope)}" +
            $"&state={Uri.EscapeDataString(state ?? "")}" +
            $"&access_type=offline&prompt=consent";
    }

    /// <summary>Exchange authorization code for tokens. Returns access+refresh tokens (secrets plaintext for caller to protect).</summary>
    public async Task<(string? AccessToken, string? RefreshToken, string? Error)> ExchangeCodeAsync(
        OAuthProvider provider, string clientId, string clientSecret, string code, string redirectUri, CancellationToken ct = default)
    {
        var client = _http.CreateClient();
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        };
        using var resp = await client.PostAsync(provider.TokenUrl,
            new FormUrlEncodedContent(form), ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) return (null, null, $"HTTP {(int)resp.StatusCode}: {body[..Math.Min(300, body.Length)]}");
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var access = root.TryGetProperty("access_token", out var at) ? at.GetString()
            : root.TryGetProperty("authed_user", out var au) && au.TryGetProperty("access_token", out var aut) ? aut.GetString() : null;
        var refresh = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
        return (access, refresh, access == null ? "No access_token in response" : null);
    }

    /// <summary>Refresh an access token using a stored refresh token.</summary>
    public async Task<string?> RefreshAccessTokenAsync(OAuthProvider provider, string clientId, string clientSecret,
        string refreshToken, CancellationToken ct = default)
    {
        var client = _http.CreateClient();
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret
        };
        using var resp = await client.PostAsync(provider.TokenUrl, new FormUrlEncodedContent(form), ct);
        if (!resp.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        return doc.RootElement.TryGetProperty("access_token", out var at) ? at.GetString() : null;
    }
}

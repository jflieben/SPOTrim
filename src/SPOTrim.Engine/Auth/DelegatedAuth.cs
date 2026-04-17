using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SPOTrim.Engine.Auth;

/// <summary>
/// OAuth2 Authorization Code flow with PKCE for delegated authentication to SharePoint and Graph.
/// </summary>
public sealed class DelegatedAuth
{
    // Multi-tenant app registration for SPOTrim (to be created in Entra ID)
    private const string ClientId = "PLACEHOLDER-SPOTRIM-CLIENT-ID";
    private const string Authority = "https://login.microsoftonline.com/common";
    private const string GraphScope = "https://graph.microsoft.com/.default offline_access openid profile";

    private readonly TokenCache _tokenCache;
    private string? _tenantId;
    private string? _tenantDomain;
    private string? _userPrincipalName;

    public bool IsConnected => _tokenCache.HasValidToken("graph") || _tokenCache.GetRefreshToken() != null;
    public string? TenantId => _tenantId;
    public string? TenantDomain => _tenantDomain;
    public string? UserPrincipalName => _userPrincipalName;
    public DateTimeOffset? RefreshTokenExpiry => _tokenCache.RefreshTokenExpiry;

    public DelegatedAuth(TokenCache tokenCache)
    {
        _tokenCache = tokenCache;
    }

    public async Task AuthenticateAsync(CancellationToken ct = default)
    {
        var codeVerifier = GenerateCodeVerifier();
        var codeChallenge = GenerateCodeChallenge(codeVerifier);

        var listener = new HttpListener();
        const int redirectPort = 1986;
        var redirectUri = $"http://localhost:{redirectPort}/";
        listener.Prefixes.Add(redirectUri);
        listener.Start();

        try
        {
            var authUrl = $"{Authority}/oauth2/v2.0/authorize" +
                $"?client_id={Uri.EscapeDataString(ClientId)}" +
                $"&response_type=code" +
                $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                $"&response_mode=query" +
                $"&scope={Uri.EscapeDataString(GraphScope)}" +
                $"&code_challenge={codeChallenge}" +
                $"&code_challenge_method=S256";

            OpenBrowser(authUrl);

            var context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromMinutes(5), ct);
            var code = context.Request.QueryString["code"];
            var error = context.Request.QueryString["error"];

            var responseHtml = code != null
                ? "<html><body><h2>Authentication successful!</h2><p>You can close this window.</p></body></html>"
                : $"<html><body><h2>Authentication failed</h2><p>{WebUtility.HtmlEncode(error)}</p></body></html>";
            var buffer = Encoding.UTF8.GetBytes(responseHtml);
            context.Response.ContentType = "text/html";
            context.Response.ContentLength64 = buffer.Length;
            await context.Response.OutputStream.WriteAsync(buffer, ct);
            context.Response.Close();

            if (string.IsNullOrEmpty(code))
                throw new InvalidOperationException($"Authentication failed: {error}");

            await ExchangeCodeForTokens(code, redirectUri, codeVerifier, ct);
            await DiscoverTenantInfoAsync(ct);
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    public void SignOut()
    {
        _tokenCache.Clear();
        _tenantId = null;
        _tenantDomain = null;
        _userPrincipalName = null;
    }

    public async Task<bool> TryRestoreSessionAsync(CancellationToken ct = default)
    {
        var refreshToken = _tokenCache.GetRefreshToken();
        if (string.IsNullOrEmpty(refreshToken))
            return false;

        try
        {
            await RefreshTokensAsync(refreshToken, ct);
            await DiscoverTenantInfoAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> GetAccessTokenAsync(string resource = "graph", CancellationToken ct = default)
    {
        var token = _tokenCache.Get(resource);
        if (token != null) return token;

        var refreshToken = _tokenCache.GetRefreshToken();
        if (string.IsNullOrEmpty(refreshToken))
            throw new InvalidOperationException("Not authenticated. Call ConnectAsync first.");

        var scope = GetScopeForResource(resource);
        await AcquireTokenForResourceAsync(refreshToken, resource, scope, ct);
        return _tokenCache.Get(resource)
            ?? throw new InvalidOperationException($"Token acquisition for '{resource}' failed.");
    }

    private string GetScopeForResource(string resource) => resource switch
    {
        "graph" => GraphScope,
        "sharepoint" => $"https://{GetSharePointHost()}/.default offline_access",
        "sharepointadmin" => $"https://{GetSharePointAdminHost()}/.default offline_access",
        _ => throw new ArgumentException($"Unknown resource: {resource}")
    };

    private string GetSharePointHost()
    {
        if (!string.IsNullOrEmpty(_tenantDomain))
        {
            var label = _tenantDomain.Split('.')[0];
            return $"{label}.sharepoint.com";
        }
        throw new InvalidOperationException("Tenant domain not available.");
    }

    private string GetSharePointAdminHost()
    {
        if (!string.IsNullOrEmpty(_tenantDomain))
        {
            var label = _tenantDomain.Split('.')[0];
            return $"{label}-admin.sharepoint.com";
        }
        throw new InvalidOperationException("Tenant domain not available.");
    }

    private async Task ExchangeCodeForTokens(string code, string redirectUri, string codeVerifier, CancellationToken ct)
    {
        using var http = new HttpClient();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier,
            ["scope"] = GraphScope
        });

        var response = await http.PostAsync($"{Authority}/oauth2/v2.0/token", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token exchange failed: {body}");

        var json = JsonDocument.Parse(body).RootElement;
        StoreTokens(json, "graph");
    }

    private async Task RefreshTokensAsync(string refreshToken, CancellationToken ct)
    {
        await AcquireTokenForResourceAsync(refreshToken, "graph", GraphScope, ct);
    }

    private async Task AcquireTokenForResourceAsync(string refreshToken, string resource, string scope, CancellationToken ct)
    {
        using var http = new HttpClient();
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = refreshToken,
            ["scope"] = scope
        });

        var response = await http.PostAsync($"{Authority}/oauth2/v2.0/token", content, ct);
        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Token refresh failed for {resource}: {body}");

        var json = JsonDocument.Parse(body).RootElement;
        StoreTokens(json, resource);
    }

    private void StoreTokens(JsonElement json, string resource)
    {
        var accessToken = json.GetProperty("access_token").GetString()!;
        var expiresIn = json.GetProperty("expires_in").GetInt32();
        _tokenCache.Set(resource, accessToken, TimeSpan.FromSeconds(expiresIn - 60));

        if (json.TryGetProperty("refresh_token", out var rt))
            _tokenCache.SetRefreshToken(rt.GetString()!);
    }

    private async Task DiscoverTenantInfoAsync(CancellationToken ct)
    {
        var token = _tokenCache.Get("graph");
        if (token == null) return;

        using var http = new HttpClient();
        http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        // Get current user info
        var meResponse = await http.GetAsync("https://graph.microsoft.com/v1.0/me?$select=userPrincipalName", ct);
        if (meResponse.IsSuccessStatusCode)
        {
            var meJson = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync(ct)).RootElement;
            _userPrincipalName = meJson.GetProperty("userPrincipalName").GetString();
        }

        // Get organization info
        var orgResponse = await http.GetAsync("https://graph.microsoft.com/v1.0/organization?$select=id,verifiedDomains", ct);
        if (orgResponse.IsSuccessStatusCode)
        {
            var orgJson = JsonDocument.Parse(await orgResponse.Content.ReadAsStringAsync(ct)).RootElement;
            if (orgJson.TryGetProperty("value", out var orgs) && orgs.GetArrayLength() > 0)
            {
                var org = orgs[0];
                _tenantId = org.GetProperty("id").GetString();

                if (org.TryGetProperty("verifiedDomains", out var domains))
                {
                    foreach (var domain in domains.EnumerateArray())
                    {
                        if (domain.TryGetProperty("isDefault", out var isDefault) && isDefault.GetBoolean())
                        {
                            _tenantDomain = domain.GetProperty("name").GetString();
                            break;
                        }
                    }
                }
            }
        }
    }

    private static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string GenerateCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // If browser fails to open, user can manually navigate
        }
    }
}

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SPOTrim.Engine.Auth;

namespace SPOTrim.Engine.Graph;

/// <summary>
/// HTTP client for Microsoft Graph API with pagination, retry, and throttling support.
/// </summary>
public sealed class GraphClient
{
    private readonly DelegatedAuth _auth;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _throttle;

    private const int DefaultMaxRetries = 5;
    private const string GraphBaseUrl = "https://graph.microsoft.com";

    public GraphClient(DelegatedAuth auth, int maxConcurrency = 5)
    {
        _auth = auth;
        _http = new HttpClient();
        _throttle = new SemaphoreSlim(maxConcurrency);
    }

    /// <summary>Paginated GET returning all pages as IAsyncEnumerable.</summary>
    public async IAsyncEnumerable<JsonElement> GetPaginatedAsync(
        string url,
        string? version = "v1.0",
        bool eventualConsistency = false,
        int maxRetries = DefaultMaxRetries,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var currentUrl = url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? url
            : $"{GraphBaseUrl}/{version}/{url.TrimStart('/')}";

        while (currentUrl != null)
        {
            ct.ThrowIfCancellationRequested();

            var (json, nextLink) = await ExecuteWithRetry(currentUrl, eventualConsistency, maxRetries, ct);
            if (json == null) yield break;

            if (json.Value.TryGetProperty("value", out var valueArray) && valueArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in valueArray.EnumerateArray())
                    yield return item;
            }
            else
            {
                yield return json.Value;
            }

            currentUrl = nextLink;
        }
    }

    /// <summary>Single GET request returning full JSON response.</summary>
    public async Task<JsonElement?> GetAsync(string url, string? version = "v1.0",
        bool eventualConsistency = false,
        int maxRetries = DefaultMaxRetries, CancellationToken ct = default)
    {
        var fullUrl = url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? url
            : $"{GraphBaseUrl}/{version}/{url.TrimStart('/')}";

        var (json, _) = await ExecuteWithRetry(fullUrl, eventualConsistency, maxRetries, ct);
        return json;
    }

    /// <summary>POST request with JSON body.</summary>
    public async Task<JsonElement?> PostAsync(string url, object body, string? version = "v1.0",
        CancellationToken ct = default)
    {
        var fullUrl = url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? url
            : $"{GraphBaseUrl}/{version}/{url.TrimStart('/')}";

        await _throttle.WaitAsync(ct);
        try
        {
            var token = await _auth.GetAccessTokenAsync("graph", ct);
            var request = new HttpRequestMessage(HttpMethod.Post, fullUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

            var response = await _http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"POST {fullUrl} failed ({response.StatusCode}): {responseBody}");

            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            return JsonDocument.Parse(responseBody).RootElement;
        }
        finally
        {
            _throttle.Release();
        }
    }

    /// <summary>PATCH request with JSON body.</summary>
    public async Task<JsonElement?> PatchAsync(string url, object body, string? version = "v1.0",
        CancellationToken ct = default)
    {
        var fullUrl = url.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? url
            : $"{GraphBaseUrl}/{version}/{url.TrimStart('/')}";

        await _throttle.WaitAsync(ct);
        try
        {
            var token = await _auth.GetAccessTokenAsync("graph", ct);
            var request = new HttpRequestMessage(HttpMethod.Patch, fullUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");

            var response = await _http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"PATCH {fullUrl} failed ({response.StatusCode}): {responseBody}");

            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            return JsonDocument.Parse(responseBody).RootElement;
        }
        finally
        {
            _throttle.Release();
        }
    }

    private async Task<(JsonElement? json, string? nextLink)> ExecuteWithRetry(
        string url, bool eventualConsistency, int maxRetries, CancellationToken ct)
    {
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            await _throttle.WaitAsync(ct);
            try
            {
                var token = await _auth.GetAccessTokenAsync("graph", ct);
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                if (eventualConsistency)
                    request.Headers.Add("ConsistencyLevel", "eventual");

                var response = await _http.SendAsync(request, ct);

                if (response.StatusCode == (HttpStatusCode)429)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta
                        ?? TimeSpan.FromSeconds(Math.Pow(5, attempt + 1));
                    await Task.Delay(retryAfter, ct);
                    continue;
                }

                if (response.StatusCode == HttpStatusCode.ServiceUnavailable ||
                    response.StatusCode == HttpStatusCode.GatewayTimeout)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt + 1)), ct);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    throw new HttpRequestException($"GET {url} failed ({response.StatusCode}): {errorBody}");
                }

                var body = await response.Content.ReadAsStringAsync(ct);
                if (string.IsNullOrWhiteSpace(body))
                    return (null, null);

                var json = JsonDocument.Parse(body).RootElement;
                string? nextLink = null;
                if (json.TryGetProperty("@odata.nextLink", out var nl))
                    nextLink = nl.GetString();

                return (json, nextLink);
            }
            finally
            {
                _throttle.Release();
            }
        }

        throw new HttpRequestException($"GET {url} failed after {maxRetries} retries");
    }
}

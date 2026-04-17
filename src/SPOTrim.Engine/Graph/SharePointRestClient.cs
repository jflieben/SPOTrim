using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using SPOTrim.Engine.Auth;

namespace SPOTrim.Engine.Graph;

/// <summary>
/// REST client for SharePoint Online REST API.
/// Uses the SharePoint-specific access token (not Graph) for site-level operations.
/// </summary>
public sealed class SharePointRestClient
{
    private readonly DelegatedAuth _auth;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _throttle;

    public SharePointRestClient(DelegatedAuth auth, int maxConcurrency = 5)
    {
        _auth = auth;
        _http = new HttpClient();
        _throttle = new SemaphoreSlim(maxConcurrency);
    }

    /// <summary>GET request to a SharePoint REST endpoint.</summary>
    public async Task<JsonElement?> GetAsync(string url, CancellationToken ct = default)
    {
        await _throttle.WaitAsync(ct);
        try
        {
            var token = await _auth.GetAccessTokenAsync("sharepoint", ct);
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _http.SendAsync(request, ct);

            if (response.StatusCode == (HttpStatusCode)429)
            {
                var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                await Task.Delay(retryAfter, ct);
                return await GetAsync(url, ct); // Retry once
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException($"SPO GET {url} failed ({response.StatusCode}): {errorBody}");
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            return string.IsNullOrWhiteSpace(body) ? null : JsonDocument.Parse(body).RootElement;
        }
        finally
        {
            _throttle.Release();
        }
    }

    /// <summary>POST request to a SharePoint REST endpoint.</summary>
    public async Task<JsonElement?> PostAsync(string url, object? body = null, CancellationToken ct = default)
    {
        await _throttle.WaitAsync(ct);
        try
        {
            var token = await _auth.GetAccessTokenAsync("sharepoint", ct);
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (body != null)
            {
                request.Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    Encoding.UTF8,
                    "application/json");
            }

            var response = await _http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"SPO POST {url} failed ({response.StatusCode}): {responseBody}");

            return string.IsNullOrWhiteSpace(responseBody) ? null : JsonDocument.Parse(responseBody).RootElement;
        }
        finally
        {
            _throttle.Release();
        }
    }
}

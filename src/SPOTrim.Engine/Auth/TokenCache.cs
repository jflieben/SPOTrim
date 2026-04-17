using System.Collections.Concurrent;

namespace SPOTrim.Engine.Auth;

/// <summary>
/// In-memory token cache with optional persistence for refresh tokens.
/// Access tokens are stored with expiry; refresh tokens are persisted to disk.
/// </summary>
public sealed class TokenCache
{
    private readonly ConcurrentDictionary<string, CachedToken> _tokens = new();
    private readonly string _persistDir;
    private string? _refreshToken;

    public DateTimeOffset? RefreshTokenExpiry { get; private set; }

    public TokenCache(string persistDir)
    {
        _persistDir = persistDir;
        LoadPersistedRefreshToken();
    }

    public void Set(string resource, string accessToken, TimeSpan lifetime)
    {
        _tokens[resource] = new CachedToken
        {
            Token = accessToken,
            ExpiresAt = DateTimeOffset.UtcNow.Add(lifetime)
        };
    }

    public string? Get(string resource)
    {
        if (_tokens.TryGetValue(resource, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
            return cached.Token;
        return null;
    }

    public bool HasValidToken(string resource)
        => Get(resource) != null;

    public void SetRefreshToken(string token)
    {
        _refreshToken = token;
        RefreshTokenExpiry = DateTimeOffset.UtcNow.AddDays(90);
        PersistRefreshToken();
    }

    public string? GetRefreshToken() => _refreshToken;

    public void Clear()
    {
        _tokens.Clear();
        _refreshToken = null;
        RefreshTokenExpiry = null;

        var path = GetRefreshTokenPath();
        if (File.Exists(path))
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    private void PersistRefreshToken()
    {
        if (_refreshToken == null) return;
        try
        {
            var path = GetRefreshTokenPath();
            // Store as base64 — not encrypted, just obfuscated
            File.WriteAllText(path, Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_refreshToken)));
        }
        catch { /* best effort */ }
    }

    private void LoadPersistedRefreshToken()
    {
        try
        {
            var path = GetRefreshTokenPath();
            if (File.Exists(path))
            {
                var encoded = File.ReadAllText(path);
                _refreshToken = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                RefreshTokenExpiry = DateTimeOffset.UtcNow.AddDays(90);
            }
        }
        catch { /* best effort */ }
    }

    private string GetRefreshTokenPath()
        => Path.Combine(_persistDir, ".spotrim-rt");

    private sealed class CachedToken
    {
        public string Token { get; set; } = "";
        public DateTimeOffset ExpiresAt { get; set; }
    }
}

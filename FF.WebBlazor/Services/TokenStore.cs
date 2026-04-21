// FF.WebBlazor/Services/TokenStore.cs
using Microsoft.JSInterop;

namespace FF.WebBlazor.Services;

/// <summary>
/// Stores JWT access + refresh tokens in localStorage so they survive
/// Blazor WASM page reloads. In-memory cache avoids repeated JS interop
/// after the first load.
/// </summary>
public class TokenStore(IJSRuntime js)
{
    private const string AccessTokenKey = "fc_access_token";
    private const string RefreshTokenKey = "fc_refresh_token";
    private const string ExpiryKey = "fc_token_expiry";

    private string? _accessToken;
    private string? _refreshToken;
    private DateTime _accessTokenExpiry;
    private bool _loaded;

    public async Task<string?> GetAccessTokenAsync()
    {
        await EnsureLoadedAsync();
        return _accessToken;
    }

    public async Task<string?> GetRefreshTokenAsync()
    {
        await EnsureLoadedAsync();
        return _refreshToken;
    }

    public async Task<DateTime> GetAccessTokenExpiryAsync()
    {
        await EnsureLoadedAsync();
        return _accessTokenExpiry;
    }

    public async Task<bool> HasValidAccessTokenAsync()
    {
        await EnsureLoadedAsync();
        return !string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _accessTokenExpiry;
    }

    public async Task<bool> HasRefreshTokenAsync()
    {
        await EnsureLoadedAsync();
        return !string.IsNullOrEmpty(_refreshToken);
    }

    public async Task SetTokensAsync(string accessToken, string refreshToken, DateTime expiry)
    {
        _accessToken = accessToken;
        _refreshToken = refreshToken;
        _accessTokenExpiry = expiry;
        _loaded = true;

        await js.InvokeVoidAsync("localStorage.setItem", AccessTokenKey, accessToken);
        await js.InvokeVoidAsync("localStorage.setItem", RefreshTokenKey, refreshToken);
        await js.InvokeVoidAsync("localStorage.setItem", ExpiryKey,
            expiry.ToString("O")); // ISO 8601 round-trip format
    }

    public async Task ClearAsync()
    {
        _accessToken = null;
        _refreshToken = null;
        _accessTokenExpiry = default;
        _loaded = true;

        await js.InvokeVoidAsync("localStorage.removeItem", AccessTokenKey);
        await js.InvokeVoidAsync("localStorage.removeItem", RefreshTokenKey);
        await js.InvokeVoidAsync("localStorage.removeItem", ExpiryKey);
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        _accessToken = await js.InvokeAsync<string?>("localStorage.getItem", AccessTokenKey);
        _refreshToken = await js.InvokeAsync<string?>("localStorage.getItem", RefreshTokenKey);

        var expiryStr = await js.InvokeAsync<string?>("localStorage.getItem", ExpiryKey);
        if (DateTime.TryParse(expiryStr, null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            _accessTokenExpiry = parsed;

        _loaded = true;
    }
}
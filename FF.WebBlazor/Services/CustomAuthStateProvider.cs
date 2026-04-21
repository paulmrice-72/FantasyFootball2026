// FF.WebBlazor/Services/CustomAuthStateProvider.cs
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace FF.WebBlazor.Services;

public class CustomAuthStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly TokenStore _tokenStore;
    private readonly AuthService _authService;
    private Timer? _refreshTimer;

    private static readonly AuthenticationState Anonymous =
        new(new ClaimsPrincipal(new ClaimsIdentity()));

    public CustomAuthStateProvider(TokenStore tokenStore, AuthService authService)
    {
        _tokenStore = tokenStore;
        _authService = authService;
        StartRefreshTimer();
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!await _tokenStore.HasValidAccessTokenAsync())
            return Anonymous;

        var token = await _tokenStore.GetAccessTokenAsync();
        var claims = ParseClaimsFromJwt(token!);
        var identity = new ClaimsIdentity(claims, "jwt");
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public async Task MarkUserAsAuthenticatedAsync(
        string accessToken, string refreshToken, DateTime expiry)
    {
        await _tokenStore.SetTokensAsync(accessToken, refreshToken, expiry);
        NotifyAuthStateChanged();
    }

    public async Task MarkUserAsLoggedOutAsync()
    {
        await _tokenStore.ClearAsync();
        NotifyAuthStateChanged();
    }

    public void NotifyAuthStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public TokenStore GetTokenStore() => _tokenStore;

    private void StartRefreshTimer()
    {
        _refreshTimer = new Timer(
            async _ => await TryRefreshTokenAsync(),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));
    }

    private async Task TryRefreshTokenAsync()
    {
        if (!await _tokenStore.HasRefreshTokenAsync()) return;

        var expiry = await _tokenStore.GetAccessTokenExpiryAsync();
        var timeUntilExpiry = expiry - DateTime.UtcNow;
        if (timeUntilExpiry > TimeSpan.FromMinutes(2)) return;

        var refreshToken = await _tokenStore.GetRefreshTokenAsync();
        var result = await _authService.RefreshAsync(refreshToken!);

        if (result is null)
        {
            await MarkUserAsLoggedOutAsync();
            return;
        }

        await _tokenStore.SetTokensAsync(
            result.AccessToken, result.RefreshToken, result.AccessTokenExpiry);
        NotifyAuthStateChanged();
    }

    private static IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
    {
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadJwtToken(jwt);
        return token.Claims;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshTimer?.Dispose();
            _refreshTimer = null;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
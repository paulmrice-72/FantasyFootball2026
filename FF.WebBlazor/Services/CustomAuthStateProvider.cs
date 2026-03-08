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

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        if (!_tokenStore.HasValidAccessToken)
            return Task.FromResult(Anonymous);

        var claims = ParseClaimsFromJwt(_tokenStore.AccessToken!);
        var identity = new ClaimsIdentity(claims, "jwt");
        var user = new ClaimsPrincipal(identity);

        return Task.FromResult(new AuthenticationState(user));
    }

    public void MarkUserAsAuthenticated(string accessToken, string refreshToken, DateTime expiry)
    {
        _tokenStore.SetTokens(accessToken, refreshToken, expiry);
        NotifyAuthStateChanged();
    }

    public void MarkUserAsLoggedOut()
    {
        _tokenStore.Clear();
        NotifyAuthStateChanged();
    }

    public void NotifyAuthStateChanged()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public TokenStore GetTokenStore() => _tokenStore;

    private void StartRefreshTimer()
    {
        _refreshTimer = new Timer(async _ => await TryRefreshTokenAsync(),
            null,
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(1));
    }

    private async Task TryRefreshTokenAsync()
    {
        if (!_tokenStore.HasRefreshToken) return;

        var timeUntilExpiry = _tokenStore.AccessTokenExpiry - DateTime.UtcNow;
        if (timeUntilExpiry > TimeSpan.FromMinutes(2)) return;

        var result = await _authService.RefreshAsync(_tokenStore.RefreshToken!);
        if (result is null)
        {
            MarkUserAsLoggedOut();
            return;
        }

        _tokenStore.SetTokens(result.AccessToken, result.RefreshToken, result.AccessTokenExpiry);
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
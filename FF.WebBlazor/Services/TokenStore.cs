namespace FF.WebBlazor.Services;

public class TokenStore
{
    private string? _accessToken;
    private string? _refreshToken;
    private DateTime _accessTokenExpiry;

    public string? AccessToken => _accessToken;
    public string? RefreshToken => _refreshToken;
    public DateTime AccessTokenExpiry => _accessTokenExpiry;

    public bool HasValidAccessToken =>
        !string.IsNullOrEmpty(_accessToken) &&
        DateTime.UtcNow < _accessTokenExpiry;

    public bool HasRefreshToken => !string.IsNullOrEmpty(_refreshToken);

    public void SetTokens(string accessToken, string refreshToken, DateTime expiry)
    {
        _accessToken = accessToken;
        _refreshToken = refreshToken;
        _accessTokenExpiry = expiry;
    }

    public void Clear()
    {
        _accessToken = null;
        _refreshToken = null;
        _accessTokenExpiry = default;
    }
}
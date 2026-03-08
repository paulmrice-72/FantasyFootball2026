using System.Net.Http.Json;
using FF.WebBlazor.Models.Auth;

namespace FF.WebBlazor.Services;

public class AuthService(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<AuthResponse?> RegisterAsync(RegisterRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/v1/auth/register", request);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<AuthResponse>();
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request)
    {
        var response = await _httpClient.PostAsJsonAsync("api/v1/auth/login", request);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<AuthResponse>();
    }

    public async Task<AuthResponse?> RefreshAsync(string refreshToken)
    {
        var response = await _httpClient.PostAsJsonAsync("api/v1/auth/refresh",
            new RefreshTokenRequest(refreshToken));
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<AuthResponse>();
    }

    public async Task RevokeAsync(string refreshToken)
    {
        await _httpClient.PostAsJsonAsync("api/v1/auth/revoke",
            new RefreshTokenRequest(refreshToken));
    }
}
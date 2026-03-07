using FF.Application.Interfaces.Auth.DTOs;
using FF.SharedKernel;
using FF.SharedKernel.Common;

namespace FF.Application.Interfaces.Auth;

public interface IAuthService
{
    Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request);
    Task<Result<AuthResponse>> LoginAsync(LoginRequest request);
    Task<Result<AuthResponse>> RefreshTokenAsync(string refreshToken);
    Task<Result> RevokeTokenAsync(string refreshToken);
}
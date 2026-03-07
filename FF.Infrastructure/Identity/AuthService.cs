using Azure.Core;
using FF.Application.Common.Settings;
using FF.Application.Interfaces.Auth;
using FF.Application.Interfaces.Auth.DTOs;
using FF.Domain.Entities;
using FF.Infrastructure.Persistence.SQL;
using FF.SharedKernel;
using FF.SharedKernel.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace FF.Infrastructure.Identity;

public class AuthService(
    UserManager<ApplicationUser> userManager,
    FFDbContext context,
    IOptions<JwtSettings> jwtSettings) : IAuthService
{
    private readonly UserManager<ApplicationUser> _userManager = userManager;
    private readonly FFDbContext _context = context;
    private readonly JwtSettings _jwtSettings = jwtSettings.Value;

    public async Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request)
    {
        var existingUser = await _userManager.FindByEmailAsync(request.Email);
        if (existingUser is not null)
            return Result.Failure<AuthResponse>(Error.Conflict("Auth.EmailTaken"));

        var user = new ApplicationUser
        {
            Email = request.Email,
            UserName = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName
        };

        var result = await _userManager.CreateAsync(user, request.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return Result.Failure<AuthResponse>(Error.Validation("RegistrationFailed", errors));
        }

        return await GenerateAuthResponseAsync(user);
    }

    public async Task<Result<AuthResponse>> LoginAsync(LoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user is null)
            return Result.Failure<AuthResponse>(Error.Unauthorized("Invalid email or password."));

        var passwordValid = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordValid)
            return Result.Failure<AuthResponse>(Error.Unauthorized("Invalid email or password."));

        return await GenerateAuthResponseAsync(user);
    }

    public async Task<Result<AuthResponse>> RefreshTokenAsync(string refreshToken)
    {
        var token = await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == refreshToken);

        if (token is null || !token.IsActive)
            return Result.Failure<AuthResponse>(Error.Unauthorized("Refresh token is invalid or expired."));

        var user = await _userManager.FindByIdAsync(token.UserId);
        if (user is null)
            return Result.Failure<AuthResponse>(Error.Unauthorized("User not found."));

        token.MarkUsed();
        await _context.SaveChangesAsync();

        return await GenerateAuthResponseAsync(user);
    }

    public async Task<Result> RevokeTokenAsync(string refreshToken)
    {
        var token = await _context.RefreshTokens
            .FirstOrDefaultAsync(t => t.Token == refreshToken);

        if (token is null || !token.IsActive)
            return Result.Failure(Error.NotFound("Auth.TokenNotFound", "Token not found or already revoked."));

        token.Revoke();
        await _context.SaveChangesAsync();

        return Result.Success();
    }

    private async Task<Result<AuthResponse>> GenerateAuthResponseAsync(ApplicationUser user)
    {
        var (Token, Expiry) = GenerateAccessToken(user);
        var refreshToken = GenerateRefreshToken();

        var refreshTokenEntity = RefreshToken.Create(
            user.Id,
            refreshToken,
            _jwtSettings.RefreshTokenExpirationDays);

        _context.RefreshTokens.Add(refreshTokenEntity);
        await _context.SaveChangesAsync();

        // In GenerateAuthResponseAsync, the return line:
        return Result.Success<AuthResponse>(new AuthResponse(
            Token,
            refreshToken,
            Expiry));
    }

    private (string Token, DateTime Expiry) GenerateAccessToken(ApplicationUser user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expiry = DateTime.UtcNow.AddMinutes(_jwtSettings.AccessTokenExpirationMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id),
            new Claim(JwtRegisteredClaimNames.Email, user.Email!),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("firstName", user.FirstName ?? string.Empty),
            new Claim("lastName", user.LastName ?? string.Empty)
        };

        var token = new JwtSecurityToken(
            issuer: _jwtSettings.Issuer,
            audience: _jwtSettings.Audience,
            claims: claims,
            expires: expiry,
            signingCredentials: credentials);

        return (new JwtSecurityTokenHandler().WriteToken(token), expiry);
    }

    private static string GenerateRefreshToken()
    {
        var randomBytes = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomBytes);
        return Convert.ToBase64String(randomBytes);
    }
}
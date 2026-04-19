using FF.Application.Interfaces.Auth;
using FF.Application.Interfaces.Auth.DTOs;
using FF.Application.Interfaces.Persistence;
using Microsoft.AspNetCore.Mvc;

namespace FF.API.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class AuthController(IAuthService authService, IPlatformSettingsRepository platformSettingsRepo) : ControllerBase
{
    private readonly IAuthService _authService = authService;
    private readonly IPlatformSettingsRepository _platformSettingsRepo = platformSettingsRepo;

    // Constructor — add IPlatformSettingsRepository platformSettingsRepo
    // and store as _platformSettingsRepo

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequest request)
    {
        var settings = await _platformSettingsRepo.GetAsync();
        if (!settings.RegistrationsEnabled)
            return StatusCode(423, new
            {
                Code = "REGISTRATIONS_CLOSED",
                Message = "New registrations are temporarily closed. Please check back soon."
            });

        var result = await _authService.RegisterAsync(request);
        if (result.IsFailure) return BadRequest(new { result.Error.Code, result.Error.Message });
        return Ok(result.Value);
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        var result = await _authService.LoginAsync(request);
        if (result.IsFailure)
            return Unauthorized(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenRequest request)
    {
        var result = await _authService.RefreshTokenAsync(request.RefreshToken);
        if (result.IsFailure)
            return Unauthorized(new { result.Error.Code, result.Error.Message });

        return Ok(result.Value);
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke([FromBody] RefreshTokenRequest request)
    {
        var result = await _authService.RevokeTokenAsync(request.RefreshToken);
        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return NoContent();
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequest request)
    {
        await _authService.ForgotPasswordAsync(request);
        return Ok(new { message = "If that email exists, a reset link has been sent." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        var result = await _authService.ResetPasswordAsync(request);
        if (result.IsFailure)
            return BadRequest(new { result.Error.Code, result.Error.Message });

        return Ok(new { message = "Password reset successfully." });
    }


}
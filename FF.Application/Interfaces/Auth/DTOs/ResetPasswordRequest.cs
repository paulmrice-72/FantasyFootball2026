// ResetPasswordRequest.cs
namespace FF.Application.Interfaces.Auth.DTOs;

public record ResetPasswordRequest(string Email, string Token, string NewPassword);
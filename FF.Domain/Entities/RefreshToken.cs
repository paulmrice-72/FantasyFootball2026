using FF.SharedKernel;

namespace FF.Domain.Entities;

public class RefreshToken : Entity
{
    public string Token { get; private set; } = string.Empty;
    public string UserId { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; }
    public bool IsUsed { get; private set; }

    public bool IsActive => !IsRevoked && !IsUsed && DateTime.UtcNow < ExpiresAt;

    public static RefreshToken Create(string userId, string token, int expirationDays)
    {
        return new RefreshToken
        {
            UserId = userId,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddDays(expirationDays)
        };
    }

    public void MarkUsed() => IsUsed = true;
    public void Revoke() => IsRevoked = true;
}
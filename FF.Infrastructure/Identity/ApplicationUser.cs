using Microsoft.AspNetCore.Identity;

namespace FF.Infrastructure.Identity;

public class ApplicationUser : IdentityUser
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Sleeper identity linking (PBI-013a)
    public string? SleeperUserId { get; set; }
    public string? SleeperUsername { get; set; }
    public DateTime? SleeperLinkedAt { get; set; }
}
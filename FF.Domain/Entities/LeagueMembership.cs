namespace FF.Domain.Entities;

public class LeagueMembership
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;        // ApplicationUser.Id (IdentityUser string key)
    public string SleeperUserId { get; set; } = string.Empty;
    public string LeagueId { get; set; } = string.Empty;      // Sleeper league ID
    public string LeagueName { get; set; } = string.Empty;
    public int Season { get; set; }
    public string Role { get; set; } = "member";              // member | commissioner
    public bool IsActive { get; set; } = true;
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;
}
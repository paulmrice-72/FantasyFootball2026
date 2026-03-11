namespace FF.Domain.Entities;

public class LeaguePrivacyRule
{
    public int Id { get; set; }
    public string LeagueId { get; set; } = string.Empty;
    public bool AllowPublicView { get; set; } = false;
    public bool AllowSharedLinks { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
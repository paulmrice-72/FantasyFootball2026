using FF.SharedKernel;

namespace FF.Domain.Entities;

/// <summary>
/// Stores per-user visibility preference for a league.
/// Hidden leagues are excluded from the nav dropdown and sync jobs.
/// </summary>
public class UserLeaguePreference : Entity
{
    public string UserId { get; private set; } = string.Empty;
    public Guid LeagueId { get; private set; }
    public bool IsHidden { get; private set; }
    public DateTime UpdatedAt { get; private set; } = DateTime.UtcNow;

    private UserLeaguePreference() { }

    public static UserLeaguePreference Create(string userId, Guid leagueId, bool isHidden) =>
        new() { UserId = userId, LeagueId = leagueId, IsHidden = isHidden };

    public void SetVisibility(bool isHidden)
    {
        IsHidden = isHidden;
        UpdatedAt = DateTime.UtcNow;
        SetUpdated();
    }
}
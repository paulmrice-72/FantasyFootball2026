using FF.SharedKernel;

namespace FF.Domain.Entities;

public class League : Entity
{
    public string Name { get; private set; } = string.Empty;
    public string SleeperLeagueId { get; private set; } = string.Empty;
    public int Season { get; private set; }
    public int TotalTeams { get; private set; }
    public bool IsActive { get; private set; }

    // Scoring settings synced from Sleeper
    public decimal RecPerReception { get; private set; } = 1m;  // 0 = standard, 0.5 = half, 1 = PPR
    public decimal PassingTdPoints { get; private set; } = 4m;  // 4 or 6
    public decimal BonusRecTe { get; private set; } = 0m;       // TE premium bonus if applicable

    private League() { }

    public static League Create(string name, string sleeperLeagueId, int season, int totalTeams)
    {
        return new League
        {
            Name = name,
            SleeperLeagueId = sleeperLeagueId,
            Season = season,
            TotalTeams = totalTeams,
            IsActive = true
        };
    }

    public void UpdateScoringSettings(decimal recPerReception, decimal passingTdPoints, decimal bonusRecTe = 0m)
    {
        RecPerReception = recPerReception;
        PassingTdPoints = passingTdPoints;
        BonusRecTe = bonusRecTe;
    }
}
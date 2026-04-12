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
    public string LeagueType { get; private set; } = "Redraft";
    public int DraftRounds { get; private set; } = 3;
    public int PickYearsOut { get; private set; } = 3;
    public bool CanTradePicks { get; private set; } = false;
    private League() { }

    public static League Create(
        string name,
        string sleeperLeagueId,
        int season,
        int totalTeams,
        string leagueType = "Redraft")   // ← new optional param
    {
        return new League
        {
            Name = name,
            SleeperLeagueId = sleeperLeagueId,
            Season = season,
            TotalTeams = totalTeams,
            IsActive = true,
            LeagueType = leagueType      // ← set it
        };
    }

    public void UpdateLeagueType(string leagueType)
    {
        LeagueType = leagueType;
        SetUpdated();
    }

    public void UpdateScoringSettings(decimal recPerReception, decimal passingTdPoints, decimal bonusRecTe = 0m)
    {
        RecPerReception = recPerReception;
        PassingTdPoints = passingTdPoints;
        BonusRecTe = bonusRecTe;
    }

    public void UpdateDraftSettings(int draftRounds, int tradePickLimit)
    {
        DraftRounds = draftRounds > 0 ? draftRounds : 3;
        CanTradePicks = tradePickLimit > 0;
        PickYearsOut = tradePickLimit > 0 ? tradePickLimit : 0;
        SetUpdated();
    }

    public void SetActiveStatus(bool isActive)
    {
        IsActive = isActive;
        SetUpdated();
    }
}
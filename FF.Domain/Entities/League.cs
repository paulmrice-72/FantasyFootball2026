// FF.Domain/Entities/League.cs
using FF.Domain.ValueObjects;
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
    public decimal RecPerReception { get; private set; } = 1m;
    public decimal PassingTdPoints { get; private set; } = 4m;
    public decimal BonusRecTe { get; private set; } = 0m;
    public string LeagueType { get; private set; } = "Redraft";

    // Draft settings
    public int DraftRounds { get; private set; } = 5;
    public int PickYearsOut { get; private set; } = 3;
    public bool CanTradePicks { get; private set; } = false;

    /// <summary>
    /// Sleeper roster_positions array stored as comma-separated string.
    /// e.g. "QB,RB,RB,WR,WR,TE,FLEX,SUPER_FLEX,BN,BN,BN,BN"
    /// Parsed into RosterConfiguration via RosterConfiguration.FromSleeperPositions().
    /// Null = not yet synced from Sleeper → callers fall back to Standard config.
    /// </summary>
    public string? RosterPositions { get; private set; }

    private League() { }

    public static League Create(
        string name,
        string sleeperLeagueId,
        int season,
        int totalTeams,
        string leagueType = "Redraft")
    {
        return new League
        {
            Name = name,
            SleeperLeagueId = sleeperLeagueId,
            Season = season,
            TotalTeams = totalTeams,
            IsActive = true,
            LeagueType = leagueType
        };
    }

    public void UpdateLeagueType(string leagueType)
    {
        LeagueType = leagueType;
        SetUpdated();
    }

    public void UpdateScoringSettings(
        decimal recPerReception,
        decimal passingTdPoints,
        decimal bonusRecTe = 0m)
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

    /// <summary>
    /// Stores the Sleeper roster_positions list as a comma-separated string.
    /// Called during league import/sync whenever Sleeper returns roster_positions.
    /// </summary>
    public void UpdateRosterPositions(IEnumerable<string> positions)
    {
        RosterPositions = string.Join(",", positions);
        SetUpdated();
    }

    /// <summary>
    /// Returns a RosterConfiguration derived from the stored Sleeper positions.
    /// Falls back to Standard config if positions have not yet been synced.
    /// </summary>
    public RosterConfiguration GetRosterConfiguration() =>
        string.IsNullOrEmpty(RosterPositions)
            ? RosterConfiguration.Standard
            : RosterConfiguration.FromSleeperPositions(
                RosterPositions.Split(','));

    public void SetActiveStatus(bool isActive)
    {
        IsActive = isActive;
        SetUpdated();
    }
}
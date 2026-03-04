using FF.SharedKernel;

namespace FF.Domain.Entities;

public class League : Entity
{
    public string Name { get; private set; } = string.Empty;
    public string SleeperLeagueId { get; private set; } = string.Empty;
    public int Season { get; private set; }
    public int TotalTeams { get; private set; }
    public bool IsActive { get; private set; }

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
}
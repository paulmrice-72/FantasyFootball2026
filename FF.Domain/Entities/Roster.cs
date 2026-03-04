using FF.SharedKernel;

namespace FF.Domain.Entities;

public class Roster : Entity
{
    public Guid LeagueId { get; private set; }
    public string OwnerName { get; private set; } = string.Empty;
    public string TeamName { get; private set; } = string.Empty;
    public string? SleeperRosterId { get; private set; }

    public League? League { get; private set; }

    private Roster() { }

    public static Roster Create(Guid leagueId, string ownerName, string teamName, string? sleeperRosterId = null)
    {
        return new Roster
        {
            LeagueId = leagueId,
            OwnerName = ownerName,
            TeamName = teamName,
            SleeperRosterId = sleeperRosterId
        };
    }
}
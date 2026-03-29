// FF.Application/Services/RosterProfileService.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;  // RosterNeed lives here now

namespace FF.Application.Services;

public class RosterProfile
{
    public string SleeperLeagueId { get; init; } = string.Empty;
    public string SleeperUserId { get; init; } = string.Empty;
    public Dictionary<string, int> RosterCountByPosition { get; init; } = [];
    public Dictionary<string, decimal> AvgFloorByPosition { get; init; } = [];
    public Dictionary<string, decimal> AvgCeilingByPosition { get; init; } = [];
    public Dictionary<string, RosterNeed> NeedByPosition { get; init; } = [];
}

//public enum RosterNeed
//{
//    Strength,   // position is well-stocked — value adds only
//    Neutral,    // average depth
//    Need        // thin at this position — prioritise adds here
//}

public class RosterProfileService(
    IRosterPlayerRepository rosterPlayerRepository,
    ISimulationResultRepository simulationResultRepository)
{
    // Minimum healthy roster depth per position
    private static readonly Dictionary<string, int> DepthTargets = new()
    {
        ["QB"] = 2,
        ["RB"] = 4,
        ["WR"] = 5,
        ["TE"] = 2
    };

    public async Task<RosterProfile?> BuildAsync(
        string sleeperLeagueId,
        string sleeperUserId,
        int season,
        int week,
        CancellationToken ct = default)
    {
        // Find this user's roster in the league
        var leagueRosters = await rosterPlayerRepository
            .GetByLeagueAsync(sleeperLeagueId, ct);

        var userRoster = leagueRosters
            .FirstOrDefault(r => r.SleeperUserId == sleeperUserId);

        if (userRoster is null) return null;

        // Load sim results for all rostered players
        var simsByPosition = new Dictionary<string, List<SimulationResultDocument>>();

        foreach (var playerId in userRoster.PlayerIds)
        {
            var sim = await simulationResultRepository
                .GetMostRecentBySleeperIdAsync(playerId, season, ct);

            if (sim is null) continue;

            if (!simsByPosition.ContainsKey(sim.Position))
                simsByPosition[sim.Position] = [];

            simsByPosition[sim.Position].Add(sim);
        }

        var positions = new[] { "QB", "RB", "WR", "TE" };
        var countByPos = new Dictionary<string, int>();
        var avgFloor = new Dictionary<string, decimal>();
        var avgCeiling = new Dictionary<string, decimal>();
        var needByPos = new Dictionary<string, RosterNeed>();

        foreach (var pos in positions)
        {
            var sims = simsByPosition.GetValueOrDefault(pos, []);
            var count = sims.Count;
            var target = DepthTargets.GetValueOrDefault(pos, 3);

            countByPos[pos] = count;
            avgFloor[pos] = sims.Count > 0 ? sims.Average(s => s.Floor) : 0m;
            avgCeiling[pos] = sims.Count > 0 ? sims.Average(s => s.Ceiling) : 0m;

            needByPos[pos] = count >= target + 1 ? RosterNeed.Strength
                           : count >= target ? RosterNeed.Neutral
                           : RosterNeed.Need;
        }

        return new RosterProfile
        {
            SleeperLeagueId = sleeperLeagueId,
            SleeperUserId = sleeperUserId,
            RosterCountByPosition = countByPos,
            AvgFloorByPosition = avgFloor,
            AvgCeilingByPosition = avgCeiling,
            NeedByPosition = needByPos
        };
    }
}
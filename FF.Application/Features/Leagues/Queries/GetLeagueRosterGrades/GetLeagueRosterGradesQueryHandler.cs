// FF.Application/Features/Leagues/Queries/GetLeagueRosterGrades/GetLeagueRosterGradesQueryHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Leagues.Queries.GetLeagueRosterGrades;

public class GetLeagueRosterGradesQueryHandler(
    IRosterPlayerRepository rosterPlayerRepository,
    IDynastyValuationRepository dynastyValuationRepository,
    IPlayerRepository playerRepository,
    ISimulationResultRepository simulationRepository,
    ILogger<GetLeagueRosterGradesQueryHandler> logger)
    : IRequestHandler<GetLeagueRosterGradesQuery, LeagueRosterGradesDto?>
{
    // 2025 data-driven baselines — median league starter at each position
    private static readonly Dictionary<string, double> PositionBaseline = new()
    {
        ["QB"] = 19.3,
        ["RB"] = 15.1,
        ["WR"] = 13.1,
        ["TE"] = 12.1
    };

    private static readonly Dictionary<string, int> StarterSlots = new()
    {
        ["QB"] = 1,
        ["RB"] = 2,
        ["WR"] = 3,
        ["TE"] = 1
    };

    // Profile thresholds — tuned to our 2025 score distribution
    // Depth ≥ 49 = strong current roster (B or better)
    // Dynasty ≥ 21 = strong future value (B or better)
    private const double DepthThreshold = 49.0;
    private const double DynastyThreshold = 21.0;

    public async Task<LeagueRosterGradesDto?> Handle(
        GetLeagueRosterGradesQuery request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Building roster grades for league {LeagueId} season {Season}",
            request.SleeperLeagueId, request.Season);

        // 1 — Load all rosters
        var rosters = await rosterPlayerRepository
            .GetByLeagueAsync(request.SleeperLeagueId, cancellationToken);
        if (rosters.Count == 0) return null;

        var allPlayerIds = rosters
            .SelectMany(r => r.PlayerIds)
            .Distinct()
            .ToList();

        // 2 — Bulk load sims, dynasty valuations, player records
        var simDocs = await simulationRepository
            .GetLatestBySleeperIdsAsync(allPlayerIds, request.Season, cancellationToken);

        var simLookup = simDocs
            .Where(s => s.SleeperPlayerId != null)
            .ToDictionary(s => s.SleeperPlayerId!, s => (double)s.Median);

        var valuations = await dynastyValuationRepository
            .GetBySleeperPlayerIdsAsync(allPlayerIds, cancellationToken);
        var valuationLookup = valuations.ToDictionary(v => v.SleeperPlayerId, v => v);

        var players = await playerRepository
            .GetBySleeperIdsAsync(allPlayerIds, cancellationToken);
        var playerLookup = players
            .Where(p => p.SleeperPlayerId != null)
            .ToDictionary(p => p.SleeperPlayerId!, p => p);

        // 3 — Grade each team
        var teams = rosters.Select(roster =>
        {
            // ── Depth Score — sim-median based ─────────────────────────────
            double totalDepthScore = 0;
            int positionsGraded = 0;

            foreach (var pos in new[] { "QB", "RB", "WR", "TE" })
            {
                var baseline = PositionBaseline[pos];
                var slots = StarterSlots[pos];

                var posPlayers = roster.PlayerIds
                    .Where(id => { playerLookup.TryGetValue(id, out var p); return p?.Position.ToString() == pos; })
                    .Select(id => simLookup.TryGetValue(id, out var m) ? m : 0.0)
                    .OrderByDescending(m => m)
                    .ToList();

                var starterScore = posPlayers.Take(slots)
                    .DefaultIfEmpty(0)
                    .Average();

                var starterNorm = baseline > 0 ? (starterScore / baseline) * 50.0 : 0;
                totalDepthScore += Math.Clamp(starterNorm, 0, 100);
                positionsGraded++;
            }

            var depthScore = positionsGraded > 0 ? totalDepthScore / positionsGraded : 0.0;

            // ── Dynasty Score — TradeValue blend ────────────────────────────
            var rosterValuations = roster.PlayerIds
                .Where(id => valuationLookup.ContainsKey(id))
                .Select(id => valuationLookup[id])
                .ToList();

            var dynastyScore = rosterValuations.Count > 0
                ? rosterValuations.Average(v =>
                    (v.TradeValue * 0.50) +
                    (v.BreakoutScore * 0.30) +
                    (Math.Min(v.YearsOfPrimeRemaining, 10) * 10.0 * 0.20))
                : 0.0;

            // ── Top assets — by sim median ───────────────────────────────────
            var topAssets = roster.PlayerIds
                .Where(id => simLookup.ContainsKey(id) && playerLookup.ContainsKey(id))
                .Select(id => (Id: id, Median: simLookup[id], Player: playerLookup[id]))
                .Where(x => new[] { "QB", "RB", "WR", "TE" }.Contains(x.Player.Position.ToString()))
                .OrderByDescending(x => x.Median)
                .Take(5)
                .Select(x =>
                {
                    valuationLookup.TryGetValue(x.Id, out var val);
                    return new TeamAssetDto(
                        PlayerName: x.Player.FullName ?? "Unknown",
                        Position: x.Player.Position.ToString() ?? "—",
                        TradeValue: Math.Round(val?.TradeValue ?? 0, 1),
                        BreakoutScore: Math.Round(val?.BreakoutScore ?? 0, 1),
                        Age: x.Player.Age);
                })
                .ToList();

            var profile = ComputeTeamProfile(depthScore, dynastyScore);

            return new TeamRosterGradeDto(
                SleeperRosterId: roster.SleeperRosterId,
                TeamName: roster.TeamName,
                OwnerName: roster.OwnerName,
                DepthGrade: DepthScoreToGrade(depthScore),
                DepthScore: Math.Round(depthScore, 1),
                DynastyGrade: DynastyScoreToGrade(dynastyScore),
                DynastyScore: Math.Round(dynastyScore, 1),
                TeamProfile: profile,
                TopAssets: topAssets);
        })
        .OrderByDescending(t => t.DepthScore)
        .ToList();

        return new LeagueRosterGradesDto(
            SleeperLeagueId: request.SleeperLeagueId,
            Season: request.Season,
            Teams: teams);
    }

    /// <summary>
    /// Quadrant logic:
    ///   High Depth + High Dynasty  = Contender
    ///   High Depth + Low Dynasty   = Win-Now
    ///   Low Depth  + High Dynasty  = Transitioning
    ///   Low Depth  + Low Dynasty   = Rebuilding
    /// </summary>
    private static string ComputeTeamProfile(double depthScore, double dynastyScore)
    {
        var strongDepth = depthScore >= DepthThreshold;
        var strongDynasty = dynastyScore >= DynastyThreshold;

        return (strongDepth, strongDynasty) switch
        {
            (true, true) => "Contender",
            (true, false) => "Win-Now",
            (false, true) => "Transitioning",
            (false, false) => "Rebuilding"
        };
    }

    // Depth grade — normalised sim-median scale (50 = league-average starter → C)
    private static string DepthScoreToGrade(double score) => score switch
    {
        >= 58 => "A",
        >= 55 => "A-",
        >= 52 => "B+",
        >= 49 => "B",
        >= 46 => "B-",
        >= 43 => "C+",
        >= 40 => "C",
        >= 35 => "C-",
        >= 28 => "D",
        _ => "F"
    };

    // Dynasty grade — TradeValue blend scale (roughly 15–35 for most rosters)
    private static string DynastyScoreToGrade(double score) => score switch
    {
        >= 35 => "A+",
        >= 30 => "A",
        >= 27 => "A-",
        >= 24 => "B+",
        >= 21 => "B",
        >= 18 => "B-",
        >= 15 => "C+",
        >= 12 => "C",
        >= 9 => "D",
        _ => "F"
    };
}
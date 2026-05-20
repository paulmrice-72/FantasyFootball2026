// FF.Application/Features/Leagues/Queries/GetLeagueRosterGrades/GetLeagueRosterGradesQueryHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Services;
using FF.Domain.Documents;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Leagues.Queries.GetLeagueRosterGrades;

public class GetLeagueRosterGradesQueryHandler(
    IRosterPlayerRepository rosterPlayerRepository,
    IDynastyValuationRepository dynastyValuationRepository,
    IPlayerRepository playerRepository,
    ISimulationResultRepository simulationRepository,
    IDepthChartRepository depthChartRepository,
    ILogger<GetLeagueRosterGradesQueryHandler> logger)
    : IRequestHandler<GetLeagueRosterGradesQuery, LeagueRosterGradesDto?>
{
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

    private const double DepthThreshold = 49.0;
    private const double DynastyThreshold = 21.0;

    private static readonly Dictionary<int, double> RoundValue = new()
    {
        [1] = 16.0,
        [2] = 8.0,
        [3] = 4.0,
        [4] = 2.0,
        [5] = 1.0
    };
    private const double YearDecayFactor = 0.85;

    public async Task<LeagueRosterGradesDto?> Handle(
        GetLeagueRosterGradesQuery request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Building roster grades for league {LeagueId} season {Season}",
            request.SleeperLeagueId, request.Season);

        var rosters = await rosterPlayerRepository
            .GetByLeagueAsync(request.SleeperLeagueId, cancellationToken);
        if (rosters.Count == 0) return null;

        var allPlayerIds = rosters.SelectMany(r => r.PlayerIds).Distinct().ToList();

        // Bulk load all data
        var simDocs = await simulationRepository
            .GetLatestBySleeperIdsAsync(allPlayerIds, request.Season, cancellationToken);
        var simLookup = simDocs
            .Where(s => s.SleeperPlayerId != null)
            .ToDictionary(s => s.SleeperPlayerId!, s => (double)s.Median);

        var valuations = await dynastyValuationRepository
            .GetBySleeperPlayerIdsAsync(allPlayerIds, cancellationToken);
        var valuationLookup = valuations.ToDictionary(v => v.SleeperPlayerId, v => v);

        var players = await playerRepository.GetBySleeperIdsAsync(allPlayerIds, cancellationToken);
        var playerLookup = players
            .Where(p => p.SleeperPlayerId != null)
            .ToDictionary(p => p.SleeperPlayerId!, p => p);

        // Load depth chart data for TE/RB players only (positions where penalty applies)
        var teRbPlayerIds = allPlayerIds
            .Where(id => playerLookup.TryGetValue(id, out var p) &&
                         (p.Position.ToString() == "TE" || p.Position.ToString() == "RB"))
            .ToList();

        var depthDocs = await depthChartRepository
            .GetLatestBySleeperIdsAsync(teRbPlayerIds, request.Season, cancellationToken);
        var depthLookup = depthDocs.ToDictionary(d => d.SleeperPlayerId, d => d);

        // Build NflTeam → TE1 age lookup for the age gate
        // (penalty is softened if the blocking TE1 is 28+)
        var te1AgeByTeam = DepthPenaltyCalculator.BuildTe1AgeByTeam(depthDocs, playerLookup.Values.ToList());

        // Compute raw draft capital scores per roster, then normalise
        var rawCapitalScores = rosters.ToDictionary(
            r => r.SleeperRosterId,
            r => ComputeRawDraftCapital(r.OwnedPicks, request.Season));
        var maxRaw = rawCapitalScores.Values.DefaultIfEmpty(1.0).Max();
        if (maxRaw < 0.001) maxRaw = 1.0;

        // Grade each team
        var teams = rosters.Select(roster =>
        {
            // Depth Score
            double totalDepthScore = 0;
            int positionsGraded = 0;

            foreach (var pos in new[] { "QB", "RB", "WR", "TE" })
            {
                var baseline = PositionBaseline[pos];
                var slots = StarterSlots[pos];

                var posPlayers = roster.PlayerIds
                    .Where(id =>
                    {
                        playerLookup.TryGetValue(id, out var p);
                        return p?.Position.ToString() == pos;
                    })
                    .Select(id => simLookup.TryGetValue(id, out var m) ? m : 0.0)
                    .OrderByDescending(m => m)
                    .ToList();

                var starterScore = posPlayers.Take(slots).DefaultIfEmpty(0).Average();

                // GRADE-FIX-002: position contributes 0 if starter quality < 50% of baseline
                var starterQualityFloor = baseline * 0.50;
                if (starterScore >= starterQualityFloor)
                {
                    var starterNorm = baseline > 0 ? (starterScore / baseline) * 50.0 : 0;
                    totalDepthScore += Math.Clamp(starterNorm, 0, 100);
                }
                positionsGraded++;
            }

            var depthScore = positionsGraded > 0 ? totalDepthScore / positionsGraded : 0.0;

            // Draft Capital Score
            var rawCapital = rawCapitalScores[roster.SleeperRosterId];
            var draftCapitalScore = Math.Round((rawCapital / maxRaw) * 100.0, 1);
            var ownedPickCount = roster.OwnedPicks.Count;

            // Dynasty Score — player blend (85%) + draft capital (15%)
            var rosterValuations = roster.PlayerIds
                .Where(id => valuationLookup.ContainsKey(id))
                .Select(id => valuationLookup[id])
                .ToList();

            double playerDynastyBlend = 0;
            if (rosterValuations.Count > 0)
            {
                var penalisedValues = rosterValuations.Select(v =>
                {
                    var penalty = DepthPenaltyCalculator.ComputeDepthPenalty(
                        v.SleeperPlayerId, v.Position, depthLookup, te1AgeByTeam);
                    var adjustedTradeValue = v.TradeValue * penalty;
                    return (adjustedTradeValue * 0.50) +
                           (v.BreakoutScore * 0.30) +
                           (Math.Min(v.YearsOfPrimeRemaining, 10) * 10.0 * 0.20);
                });
                playerDynastyBlend = penalisedValues.Average();
            }

            var dynastyScore = (playerDynastyBlend * 0.85) + (draftCapitalScore * 0.35 * 0.15);

            // Top assets by sim median (unpenalised — these are raw assets)
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
                DraftCapitalScore: draftCapitalScore,
                OwnedPickCount: ownedPickCount,
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
    /// Returns a 0.0–1.0 multiplier to apply to TradeValue for dynasty scoring.
    /// Only penalises TE and RB non-starters. QB/WR unaffected.
    /// Age gate: if the blocking TE1 is 28+, penalty is softened by 50%
    /// (the blocker may age out soon, so the backup has more upside).
    /// </summary>
    
    private static double ComputeRawDraftCapital(List<RosterPickDto> picks, int currentSeason)
    {
        var total = 0.0;
        foreach (var pick in picks)
        {
            var roundVal = RoundValue.TryGetValue(pick.Round, out var rv) ? rv : 0.5;
            var yearsOut = Math.Max(0, pick.Season - currentSeason);
            total += roundVal * Math.Pow(YearDecayFactor, yearsOut);
        }
        return total;
    }

    private static string ComputeTeamProfile(double depthScore, double dynastyScore) =>
        (depthScore >= DepthThreshold, dynastyScore >= DynastyThreshold) switch
        {
            (true, true) => "Contender",
            (true, false) => "Win-Now",
            (false, true) => "Transitioning",
            (false, false) => "Rebuilding"
        };

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
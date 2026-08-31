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

        // Compute raw scores for each team. Grades are assigned in a second
        // pass below, RELATIVE to the other teams in this league — see the
        // note above where RosterStrengthCalculator.RankFractionToGrade is
        // called for why.
        var rawTeams = rosters.Select(roster =>
        {
            // Depth Score — FAN-107: shared with the redraft handler via
            // RosterStrengthCalculator so the two can't drift apart.
            var depthScore = RosterStrengthCalculator.ComputeRawDepthScore(
                roster.PlayerIds, playerLookup, simLookup);

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

            // Bug fix: this was previously `draftCapitalScore * 0.35 * 0.15`
            // (≈5.25% weight instead of the intended 15%), leaving draft
            // capital almost meaningless in the blend.
            var dynastyScore = (playerDynastyBlend * 0.85) + (draftCapitalScore * 0.15);

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

            return (
                roster.SleeperRosterId, roster.TeamName, roster.OwnerName,
                DepthScore: depthScore, DynastyScore: dynastyScore,
                DraftCapitalScore: draftCapitalScore, OwnedPickCount: ownedPickCount,
                TopAssets: topAssets);
        })
        .ToList();

        // ── Grade RELATIVE to this league, not against a fixed score ──────
        // FAN-95 follow-up (2026-08-26): DepthScoreToGrade/DynastyScoreToGrade
        // used to be fixed absolute cutoffs. Those were calibrated once
        // (GRADE-FIX-001, May) against whatever TradeValue/score distribution
        // existed that day — and drifted every time the DFV pipeline's scale
        // changed since (P2 exponent 0.6→0.9 on 5/19, guardrails added on
        // 8/25), silently pushing every team toward the same grade again.
        // Percentile-within-league is self-correcting — see
        // RosterStrengthCalculator.RankFractionToGrade.
        var depthRank = RosterStrengthCalculator.RankByDescending(rawTeams, t => t.DepthScore);
        var dynastyRank = RosterStrengthCalculator.RankByDescending(rawTeams, t => t.DynastyScore);

        var teams = rawTeams.Select((t, idx) =>
        {
            var depthFraction = depthRank[idx];
            var dynastyFraction = dynastyRank[idx];
            var profile = ComputeTeamProfile(depthFraction, dynastyFraction);

            return new TeamRosterGradeDto(
                SleeperRosterId: t.SleeperRosterId,
                TeamName: t.TeamName,
                OwnerName: t.OwnerName,
                DepthGrade: RosterStrengthCalculator.RankFractionToGrade(depthFraction),
                DepthScore: Math.Round(t.DepthScore, 1),
                DynastyGrade: RosterStrengthCalculator.RankFractionToGrade(dynastyFraction),
                DynastyScore: Math.Round(t.DynastyScore, 1),
                TeamProfile: profile,
                DraftCapitalScore: t.DraftCapitalScore,
                OwnedPickCount: t.OwnedPickCount,
                TopAssets: t.TopAssets);
        })
        .OrderByDescending(t => t.DepthScore)
        .ToList();

        return new LeagueRosterGradesDto(
            SleeperLeagueId: request.SleeperLeagueId,
            Season: request.Season,
            Teams: teams);
    }

    /// <summary>
    /// Sums pick-round value (round 1 = 16, decaying by round), discounted
    /// by how many years out the pick is. Used as the draft capital
    /// component of DynastyScore.
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

    /// <summary>
    /// Profile is based on whether a team sits in the top half of ITS
    /// league on depth (win-now production) vs dynasty (future value) —
    /// same self-correcting rationale as the grade fractions above, instead
    /// of comparing against a fixed score that goes stale.
    /// </summary>
    private static string ComputeTeamProfile(double depthRankFraction, double dynastyRankFraction) =>
        (depthRankFraction <= 0.5, dynastyRankFraction <= 0.5) switch
        {
            (true, true) => "Contender",
            (true, false) => "Win-Now",
            (false, true) => "Transitioning",
            (false, false) => "Rebuilding"
        };
}

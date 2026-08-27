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
        // note above DepthScoreToGrade for why.
        var rawTeams = rosters.Select(roster =>
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
        // Percentile-within-league is self-correcting: it always reflects
        // "how does my team compare to the ~10-14 others in THIS league",
        // which is what the page is actually for, and never needs
        // recalibrating when the underlying score scale moves.
        var depthRank = RankByDescending(rawTeams, t => t.DepthScore);
        var dynastyRank = RankByDescending(rawTeams, t => t.DynastyScore);

        var teams = rawTeams.Select((t, idx) =>
        {
            var depthFraction = depthRank[idx];
            var dynastyFraction = dynastyRank[idx];
            var profile = ComputeTeamProfile(depthFraction, dynastyFraction);

            return new TeamRosterGradeDto(
                SleeperRosterId: t.SleeperRosterId,
                TeamName: t.TeamName,
                OwnerName: t.OwnerName,
                DepthGrade: RankFractionToGrade(depthFraction),
                DepthScore: Math.Round(t.DepthScore, 1),
                DynastyGrade: RankFractionToGrade(dynastyFraction),
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
    /// Ranks items by a score (descending — best first) and returns each
    /// item's rank fraction in the SAME ORDER as the input list: 0.0 = best
    /// in the league, 1.0 = worst. Ties share the same fraction (average
    /// rank) so two teams with an identical score get the same grade
    /// instead of an arbitrary tiebreak deciding one is a letter grade
    /// better than the other.
    /// </summary>
    private static double[] RankByDescending<T>(List<T> items, Func<T, double> selector)
    {
        var n = items.Count;
        var fractions = new double[n];
        if (n <= 1) return fractions; // single team in the league — no basis for relative grading

        var order = Enumerable.Range(0, n)
            .OrderByDescending(i => selector(items[i]))
            .ToList();

        var i = 0;
        while (i < n)
        {
            var j = i;
            while (j < n && selector(items[order[j]]).Equals(selector(items[order[i]]))) j++;
            var avgRank = (i + j - 1) / 2.0; // 0-based average rank across the tied group
            var fraction = avgRank / (n - 1);
            for (var k = i; k < j; k++) fractions[order[k]] = fraction;
            i = j;
        }

        return fractions;
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
    /// Profile is now based on whether a team sits in the top half of ITS
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

    /// <summary>
    /// Maps a within-league rank fraction (0.0 = best team in the league,
    /// 1.0 = worst) to a letter grade. Replaces the old fixed-score cutoffs
    /// in DepthScoreToGrade/DynastyScoreToGrade — see the note above where
    /// this is called. Buckets are sized for a typical 10-14 team dynasty
    /// league (roughly: top ~1 team A+, next ~2 A, etc.), tunable via the
    /// calibration harness like everything else in this pipeline.
    /// </summary>
    private static string RankFractionToGrade(double rankFraction) => rankFraction switch
    {
        <= 0.08 => "A+",
        <= 0.20 => "A",
        <= 0.35 => "B+",
        <= 0.50 => "B",
        <= 0.65 => "C+",
        <= 0.80 => "C",
        <= 0.92 => "D",
        _ => "F"
    };
}
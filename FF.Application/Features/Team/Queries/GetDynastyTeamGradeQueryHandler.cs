// FF.Application/Features/Team/Queries/GetDynastyTeamGradeQueryHandler.cs
using FF.Application.Features.Leagues.Queries.GetLeagueRosterGrades;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Services;
using FF.Domain.Documents;
using FF.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Team.Queries;

public class GetDynastyTeamGradeQueryHandler(
    IRosterPlayerRepository rosterPlayerRepository,
    IDynastyValuationRepository dynastyValuationRepository,
    IDepthChartRepository depthChartRepository,
    IPlayerRepository playerRepository,
    ILogger<GetDynastyTeamGradeQueryHandler> logger)
    : IRequestHandler<GetDynastyTeamGradeQuery, DynastyTeamGradeDto?>
{
    private static readonly (double Min, string Grade, string Label)[] GradeTable =
    [
        (88, "A+", "Elite"),
        (75, "A",  "Excellent"),
        (62, "B+", "Strong"),
        (50, "B",  "Solid"),
        (38, "C+", "Average"),
        (26, "C",  "Below Average"),
        (15, "D",  "Weak"),
        (0,  "F",  "Dire")
    ];

    private static readonly HashSet<CareerPhase> PrimePhases = [CareerPhase.Prime];
    private static readonly HashSet<CareerPhase> YoungPhases = [CareerPhase.Ascending];

    public async Task<DynastyTeamGradeDto?> Handle(
        GetDynastyTeamGradeQuery request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Computing dynasty team grade for user {UserId} league {LeagueId}",
            request.SleeperUserId, request.SleeperLeagueId);

        // 1 — Load roster (same as original — always by SleeperUserId)
        var rosterDoc = await rosterPlayerRepository.GetBySleeperUserIdAsync(
            request.SleeperUserId,
            request.SleeperLeagueId, cancellationToken);

        if (rosterDoc is null || rosterDoc.PlayerIds.Count == 0) return null;

        // 2 — Bulk load dynasty valuations
        var valuations = await dynastyValuationRepository.GetBySleeperPlayerIdsAsync(
            rosterDoc.PlayerIds, cancellationToken);
        if (valuations.Count == 0) return null;

        // 3 — Load depth chart data for TE/RB players (penalty positions only)
        var teRbIds = rosterDoc.PlayerIds
            .Where(id => valuations.Any(v => v.SleeperPlayerId == id &&
                                            (v.Position == "TE" || v.Position == "RB")))
            .ToList();

        var depthDocs = await depthChartRepository
            .GetLatestBySleeperIdsAsync(teRbIds, DateTime.UtcNow.Year, cancellationToken);
        var depthLookup = depthDocs.ToDictionary(d => d.SleeperPlayerId, d => d);

        // 4 — Build TE1 age lookup for the age gate
        var allPlayers = await playerRepository
            .GetBySleeperIdsAsync(rosterDoc.PlayerIds, cancellationToken);
        var playerLookup = allPlayers
            .Where(p => p.SleeperPlayerId != null)
            .ToDictionary(p => p.SleeperPlayerId!, p => p);

        var te1AgeByTeam = DepthPenaltyCalculator.BuildTe1AgeByTeam(depthDocs, playerLookup.Values.ToList());

        // 5 — Partition into prime / young / veteran
        var primeGroup = valuations
            .Where(v => PrimePhases.Contains(v.CareerPhase))
            .OrderByDescending(v => v.TradeValue)
            .ToList();

        var youngGroup = valuations
            .Where(v => YoungPhases.Contains(v.CareerPhase))
            .OrderByDescending(v => v.TradeValue)
            .ToList();

        var veteranGroup = valuations
            .Where(v => !PrimePhases.Contains(v.CareerPhase) &&
                        !YoungPhases.Contains(v.CareerPhase))
            .ToList();

        // 6 — Contention score
        // Prime players at full weight (depth-adjusted), veterans at 40%, young at 20%
        double contentionRaw = 0;
        double contentionWeight = 0;

        foreach (var v in primeGroup)
        {
            var penalty = DepthPenaltyCalculator.ComputeDepthPenalty(
                v.SleeperPlayerId, v.Position, depthLookup, te1AgeByTeam);
            contentionRaw += (v.TradeValue * penalty) * 1.0;
            contentionWeight += 1.0;
        }
        foreach (var v in veteranGroup)
        {
            var penalty = DepthPenaltyCalculator.ComputeDepthPenalty(
                v.SleeperPlayerId, v.Position, depthLookup, te1AgeByTeam);
            contentionRaw += (v.TradeValue * penalty) * 0.4;
            contentionWeight += 0.4;
        }
        foreach (var v in youngGroup)
        {
            var penalty = DepthPenaltyCalculator.ComputeDepthPenalty(
                v.SleeperPlayerId, v.Position, depthLookup, te1AgeByTeam);
            contentionRaw += (v.TradeValue * penalty) * 0.2;
            contentionWeight += 0.2;
        }

        var contentionScore = contentionWeight > 0
            ? Math.Clamp(contentionRaw / contentionWeight, 0, 100)
            : 0;

        // 7 — Longevity score
        // Young players at full weight + BreakoutScore boost; prime at 30%
        double longevityRaw = 0;
        double longevityWeight = 0;

        foreach (var v in youngGroup)
        {
            var penalty = DepthPenaltyCalculator.ComputeDepthPenalty(
                v.SleeperPlayerId, v.Position, depthLookup, te1AgeByTeam);
            var boosted = ((v.TradeValue * penalty) * 0.70) + (v.BreakoutScore * 0.30);
            var primeBoost = Math.Min(1.5, 1.0 + (v.YearsOfPrimeRemaining * 0.05));
            longevityRaw += boosted * primeBoost * 1.0;
            longevityWeight += 1.0;
        }
        foreach (var v in primeGroup)
        {
            var penalty = DepthPenaltyCalculator.ComputeDepthPenalty(
                v.SleeperPlayerId, v.Position, depthLookup, te1AgeByTeam);
            longevityRaw += (v.TradeValue * penalty) * 0.30;
            longevityWeight += 0.30;
        }

        var longevityScore = longevityWeight > 0
            ? Math.Clamp(longevityRaw / longevityWeight, 0, 100)
            : 0;

        // 8 — Grade + profile
        var (contentionGrade, contentionLabel) = MapGrade((int)Math.Round(contentionScore));
        var (longevityGrade, longevityLabel) = MapGrade((int)Math.Round(longevityScore));
        var profile = DetermineProfile(contentionScore, longevityScore);

        // 9 — Summaries
        var contentionSummary = BuildContentionSummary(
            contentionGrade, primeGroup.Count, contentionScore);
        var longevitySummary = BuildLongevitySummary(
            longevityGrade, youngGroup.Count, longevityScore);

        var avgAge = valuations.Average(v => (double)v.Age);
        var avgTradeValue = valuations.Average(v => v.TradeValue);

        return new DynastyTeamGradeDto(
            ContentionGrade: contentionGrade,
            ContentionScore: (int)Math.Round(contentionScore),
            ContentionLabel: contentionLabel,
            ContentionSummary: contentionSummary,
            LongevityGrade: longevityGrade,
            LongevityScore: (int)Math.Round(longevityScore),
            LongevityLabel: longevityLabel,
            LongevitySummary: longevitySummary,
            OverallProfile: profile,
            RosteredCount: valuations.Count,
            PrimePlayerCount: primeGroup.Count,
            YoungPlayerCount: youngGroup.Count,
            AvgTradeValue: Math.Round(avgTradeValue, 1),
            AvgAge: Math.Round(avgAge, 1));
    }

    // ── Static helpers ────────────────────────────────────────────────────────

    private static (string Grade, string Label) MapGrade(int score)
    {
        foreach (var (min, grade, label) in GradeTable)
            if (score >= min) return (grade, label);
        return ("F", "Dire");
    }

    private static string DetermineProfile(double contention, double longevity)
    {
        var gap = contention - longevity;
        return (contention, longevity, gap) switch
        {
            _ when contention >= 65 && longevity >= 65 => "Dynasty Powerhouse",
            _ when gap >= 20 => "Win-Now",
            _ when gap <= -20 => "Rebuilding",
            _ when contention >= 50 && longevity >= 50 => "Balanced Contender",
            _ => "Transitioning"
        };
    }

    private static string BuildContentionSummary(string grade, int primeCount, double score) =>
        grade switch
        {
            "A+" or "A" => $"Elite win-now roster — {primeCount} prime-age impact players driving contention.",
            "B+" or "B" => $"Solid contender — {primeCount} prime player(s) anchoring the roster.",
            "C+" or "C" => "Average contention window — limited prime-age talent on the roster.",
            "D" => "Thin win-now talent. Consider targeting proven starters via trade.",
            _ => "No meaningful contention window. Full rebuild or aggressive trade-up needed."
        };

    private static string BuildLongevitySummary(string grade, int youngCount, double score) =>
        grade switch
        {
            "A+" or "A" => $"Outstanding future — {youngCount} ascending/prospect players with high upside.",
            "B+" or "B" => $"Good long-term outlook — {youngCount} young piece(s) with dynasty value.",
            "C+" or "C" => "Average dynasty depth. Adding young talent via draft or trade would help.",
            "D" => "Aging roster with limited future value. Prioritise young players in trades.",
            _ => "No dynasty assets. Immediate youth infusion required."
        };
}
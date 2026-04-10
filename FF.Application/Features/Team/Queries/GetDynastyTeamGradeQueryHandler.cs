// FF.Application/Features/Team/Queries/GetDynastyTeamGradeQueryHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Domain.Enums;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Team.Queries;

public class GetDynastyTeamGradeQueryHandler(
    IRosterPlayerRepository rosterPlayerRepository,
    IDynastyValuationRepository dynastyValuationRepository,
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

    private static readonly HashSet<CareerPhase> PrimePhases =
            [CareerPhase.Prime];

    private static readonly HashSet<CareerPhase> YoungPhases =
        [CareerPhase.Ascending];

    public async Task<DynastyTeamGradeDto?> Handle(
        GetDynastyTeamGradeQuery request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Computing dynasty team grade for user {UserId} league {LeagueId}",
            request.SleeperUserId, request.SleeperLeagueId);

        // 1 — Load roster
        var rosterDoc = await rosterPlayerRepository.GetBySleeperUserIdAsync(
            request.SleeperUserId, request.SleeperLeagueId, cancellationToken);

        if (rosterDoc is null || rosterDoc.PlayerIds.Count == 0)
            return null;

        // 2 — Bulk load dynasty valuations (same pattern as depth grades)
        var valuations = await dynastyValuationRepository.GetBySleeperPlayerIdsAsync(
            rosterDoc.PlayerIds, cancellationToken);

        if (valuations.Count == 0)
            return null;

        // 3 — Partition into prime (win-now) vs young (longevity)
        var primeGroup = valuations
            .Where(v => PrimePhases.Contains(v.CareerPhase))
            .OrderByDescending(v => v.TradeValue)
            .ToList();

        var youngGroup = valuations
            .Where(v => YoungPhases.Contains(v.CareerPhase))
            .OrderByDescending(v => v.TradeValue)
            .ToList();

        // Declining / Veteran players contribute modestly to contention only
        var veteranGroup = valuations
            .Where(v => !PrimePhases.Contains(v.CareerPhase) &&
                        !YoungPhases.Contains(v.CareerPhase))
            .ToList();

        // 4 — Contention score
        // Prime players at full weight, veterans at 40%, young at 20%
        double contentionRaw = 0;
        double contentionWeight = 0;

        foreach (var v in primeGroup)
        {
            contentionRaw += v.TradeValue * 1.0;
            contentionWeight += 1.0;
        }
        foreach (var v in veteranGroup)
        {
            contentionRaw += v.TradeValue * 0.4;
            contentionWeight += 0.4;
        }
        foreach (var v in youngGroup)
        {
            contentionRaw += v.TradeValue * 0.2;
            contentionWeight += 0.2;
        }

        var contentionScore = contentionWeight > 0
            ? Math.Clamp(contentionRaw / contentionWeight, 0, 100)
            : 0;

        // 5 — Longevity score
        // Young players at full weight, boosted by BreakoutScore and YearsOfPrimeRemaining
        // Prime players contribute at 30% (they're good now but ageing)
        double longevityRaw = 0;
        double longevityWeight = 0;

        foreach (var v in youngGroup)
        {
            // Boost: blend TradeValue (70%) + BreakoutScore (30%)
            var boosted = (v.TradeValue * 0.70) + (v.BreakoutScore * 0.30);
            // Extra multiplier for years of prime remaining
            var primeBoost = Math.Min(1.5, 1.0 + (v.YearsOfPrimeRemaining * 0.05));
            longevityRaw += boosted * primeBoost * 1.0;
            longevityWeight += 1.0;
        }
        foreach (var v in primeGroup)
        {
            longevityRaw += v.TradeValue * 0.30;
            longevityWeight += 0.30;
        }

        var longevityScore = longevityWeight > 0
            ? Math.Clamp(longevityRaw / longevityWeight, 0, 100)
            : 0;

        // 6 — Grade both
        var (contentionGrade, contentionLabel) = MapGrade((int)Math.Round(contentionScore));
        var (longevityGrade, longevityLabel) = MapGrade((int)Math.Round(longevityScore));

        // 7 — Overall profile
        var profile = DetermineProfile(contentionScore, longevityScore);

        // 8 — Summaries
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

    // ── Helpers ───────────────────────────────────────────────────────────────

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

    private static string BuildContentionSummary(
        string grade, int primeCount, double score) => grade switch
        {
            "A+" or "A" =>
                $"Elite win-now roster — {primeCount} prime-age impact players driving contention.",
            "B+" or "B" =>
                $"Solid contender — {primeCount} prime player(s) anchoring the roster.",
            "C+" or "C" =>
                $"Average contention window — limited prime-age talent on the roster.",
            "D" =>
                "Thin win-now talent. Consider targeting proven starters via trade.",
            _ =>
                "No meaningful contention window. Full rebuild or aggressive trade-up needed."
        };

    private static string BuildLongevitySummary(
        string grade, int youngCount, double score) => grade switch
        {
            "A+" or "A" =>
                $"Outstanding future — {youngCount} ascending/prospect players with high upside.",
            "B+" or "B" =>
                $"Good long-term outlook — {youngCount} young piece(s) with dynasty value.",
            "C+" or "C" =>
                "Average dynasty depth. Adding young talent via draft or trade would help.",
            "D" =>
                "Aging roster with limited future value. Prioritise young players in trades.",
            _ =>
                "No dynasty assets. Immediate youth infusion required."
        };
}
using FF.Application.Features.Dynasty.Commands;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace FF.Infrastructure.Services;

public class TradeAnalyzerService(
    IDynastyValuationRepository valuationRepository,
    IPickValueRepository pickValueRepository,
    ILogger<TradeAnalyzerService> logger) : ITradeAnalyzerService
{
    public async Task<TradeAnalysisDocument> AnalyzeAsync(
        string userId,
        IEnumerable<string> myPlayerSleeperIds,
        IEnumerable<string> theirPlayerSleeperIds,
        int season,
        CancellationToken ct = default) =>
        await AnalyzeAsync(userId, myPlayerSleeperIds, theirPlayerSleeperIds, [], [], season, ct);

    public async Task<TradeAnalysisDocument> AnalyzeAsync(
        string userId,
        IEnumerable<string> myPlayerSleeperIds,
        IEnumerable<string> theirPlayerSleeperIds,
        IEnumerable<TradePickRequest> myPicks,
        IEnumerable<TradePickRequest> theirPicks,
        int season,
        CancellationToken ct = default)
    {
        var myIds = myPlayerSleeperIds.ToList();
        var theirIds = theirPlayerSleeperIds.ToList();

        var mySide = await BuildSideAsync(myIds, myPicks.ToList(), ct);
        var theirSide = await BuildSideAsync(theirIds, theirPicks.ToList(), ct);

        var myValue = mySide.Sum(p => p.TradeValue);
        var theirValue = theirSide.Sum(p => p.TradeValue);
        var diff = myValue - theirValue;

        var grade = ComputeGrade(diff);
        var recommendation = BuildRecommendation(grade, diff);
        var insights = BuildInsights(mySide, theirSide, diff);

        logger.LogInformation(
            "Trade analyzed for {UserId} — my {MyValue:F1} vs their {TheirValue:F1} — grade {Grade}",
            userId, myValue, theirValue, grade);

        return new TradeAnalysisDocument
        {
            Id = ObjectId.GenerateNewId().ToString(),
            UserId = userId,
            AnalyzedAt = DateTime.UtcNow,
            Season = season,
            MySide = mySide,
            TheirSide = theirSide,
            MySideValue = Math.Round(myValue, 2),
            TheirSideValue = Math.Round(theirValue, 2),
            ValueDifferential = Math.Round(diff, 2),
            Grade = grade,
            Recommendation = recommendation,
            KeyInsights = insights
        };
    }

    private async Task<List<TradeSideDetail>> BuildSideAsync(
        List<string> sleeperIds,
        List<TradePickRequest> picks,
        CancellationToken ct)
    {
        var side = new List<TradeSideDetail>();

        // Players
        foreach (var id in sleeperIds)
        {
            var valuation = await valuationRepository.GetBySleeperIdAsync(id, ct);

            if (valuation is null)
            {
                logger.LogWarning("No dynasty valuation found for SleeperPlayerId {Id}", id);
                side.Add(new TradeSideDetail
                {
                    SleeperPlayerId = id,
                    PlayerName = "Unknown Player",
                    TradeValue = 0
                });
                continue;
            }

            side.Add(new TradeSideDetail
            {
                SleeperPlayerId = id,
                PlayerName = valuation.PlayerName,
                Position = valuation.Position,
                Age = valuation.Age,
                TradeValue = valuation.TradeValue,
                BreakoutScore = valuation.BreakoutScore,
                BreakoutClassification = valuation.BreakoutClassification.ToString(),
                YearsOfPrimeRemaining = valuation.YearsOfPrimeRemaining
            });
        }

        // Draft picks
        foreach (var pick in picks)
        {
            var pickDoc = await pickValueRepository.GetAsync(pick.Round, pick.Tier, pick.Year, ct);
            var value = pickDoc?.Value ?? 0;
            var label = $"{pick.Year} {pick.Tier} {OrdinalRound(pick.Round)}";

            side.Add(new TradeSideDetail
            {
                SleeperPlayerId = string.Empty,
                PlayerName = label,
                Position = "PICK",
                TradeValue = value,
                IsDraftPick = true,
                PickRound = pick.Round,
                PickTier = pick.Tier,
                PickYear = pick.Year
            });
        }

        return side;
    }

    private static string OrdinalRound(int round) => round switch
    {
        1 => "1st",
        2 => "2nd",
        3 => "3rd",
        _ => $"{round}th"
    };

    private static string ComputeGrade(double differential) => differential switch
    {
        <= -15 => "A",
        <= -8 => "B",
        <= 7 => "C",
        <= 14 => "D",
        _ => "F"
    };

    private static string BuildRecommendation(string grade, double diff) => grade switch
    {
        "A" => $"Strong accept — you gain {Math.Abs(diff):F1} value points.",
        "B" => $"Accept — you win by {Math.Abs(diff):F1} value points.",
        "C" => "Even trade — consider player fit and roster needs.",
        "D" => $"Decline — you lose {diff:F1} value points.",
        "F" => $"Hard pass — you lose {diff:F1} value points.",
        _ => "Unable to evaluate."
    };

    private static List<string> BuildInsights(
        List<TradeSideDetail> mySide,
        List<TradeSideDetail> theirSide,
        double diff)
    {
        var insights = new List<string>();

        var myPlayers = mySide.Where(p => !p.IsDraftPick).ToList();
        var theirPlayers = theirSide.Where(p => !p.IsDraftPick).ToList();
        var myPickCount = mySide.Count(p => p.IsDraftPick);
        var theirPickCount = theirSide.Count(p => p.IsDraftPick);

        // Pick notes
        if (theirPickCount > myPickCount)
            insights.Add($"You're acquiring {theirPickCount} draft pick(s) — future capital.");
        else if (myPickCount > theirPickCount)
            insights.Add($"You're trading away {myPickCount} draft pick(s) — selling future capital.");

        // Age
        if (myPlayers.Count > 0 && theirPlayers.Count > 0)
        {
            var myAvgAge = myPlayers.Average(p => p.Age);
            var theirAvgAge = theirPlayers.Average(p => p.Age);

            if (theirAvgAge < myAvgAge - 2)
                insights.Add($"You're buying younger — avg age {theirAvgAge:F0} vs {myAvgAge:F0}.");
            else if (myAvgAge < theirAvgAge - 2)
                insights.Add($"You're selling younger — avg age {myAvgAge:F0} vs {theirAvgAge:F0}.");
        }

        // Breakout
        var myBreakouts = myPlayers.Count(p => p.BreakoutClassification == "Breakout");
        var theirBreakouts = theirPlayers.Count(p => p.BreakoutClassification == "Breakout");

        if (theirBreakouts > myBreakouts)
            insights.Add($"You're acquiring {theirBreakouts} breakout candidate(s).");
        else if (myBreakouts > theirBreakouts)
            insights.Add($"You're trading away {myBreakouts} breakout candidate(s).");

        // Prime years
        var myPrime = myPlayers.Sum(p => p.YearsOfPrimeRemaining);
        var theirPrime = theirPlayers.Sum(p => p.YearsOfPrimeRemaining);

        if (theirPrime > myPrime + 2)
            insights.Add($"More prime years incoming ({theirPrime:F0} vs {myPrime:F0}).");
        else if (myPrime > theirPrime + 2)
            insights.Add($"Trading away prime years ({myPrime:F0} vs {theirPrime:F0}).");

        return insights;
    }
}
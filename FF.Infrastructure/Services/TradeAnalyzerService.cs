using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace FF.Infrastructure.Services;

public class TradeAnalyzerService(
    IDynastyValuationRepository valuationRepository,
    ILogger<TradeAnalyzerService> logger) : ITradeAnalyzerService
{
    public async Task<TradeAnalysisDocument> AnalyzeAsync(
        string userId,
        IEnumerable<string> myPlayerSleeperIds,
        IEnumerable<string> theirPlayerSleeperIds,
        int season,
        CancellationToken ct = default)
    {
        var myIds = myPlayerSleeperIds.ToList();
        var theirIds = theirPlayerSleeperIds.ToList();

        var mySide = await BuildSideAsync(myIds, ct);
        var theirSide = await BuildSideAsync(theirIds, ct);

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

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task<List<TradeSideDetail>> BuildSideAsync(
        List<string> sleeperIds, CancellationToken ct)
    {
        var side = new List<TradeSideDetail>();

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

        return side;
    }

    private static string ComputeGrade(double differential) => differential switch
    {
        >= 15 => "A",
        >= 8 => "B",
        >= -7 => "C",
        >= -14 => "D",
        _ => "F"
    };

    private static string BuildRecommendation(string grade, double diff) => grade switch
    {
        "A" => $"Strong accept — you gain {diff:F1} value points.",
        "B" => $"Accept — you win by {diff:F1} value points.",
        "C" => "Even trade — consider player fit and roster needs.",
        "D" => $"Decline — you lose {Math.Abs(diff):F1} value points.",
        "F" => $"Hard pass — you lose {Math.Abs(diff):F1} value points.",
        _ => "Unable to evaluate."
    };

    private static List<string> BuildInsights(
        List<TradeSideDetail> mySide,
        List<TradeSideDetail> theirSide,
        double diff)
    {
        var insights = new List<string>();

        // Age advantage
        var myAvgAge = mySide.Count > 0 ? mySide.Average(p => p.Age) : 0;
        var theirAvgAge = theirSide.Count > 0 ? theirSide.Average(p => p.Age) : 0;

        if (myAvgAge < theirAvgAge - 2)
            insights.Add($"You're buying younger — avg age {myAvgAge:F0} vs {theirAvgAge:F0}.");
        else if (theirAvgAge < myAvgAge - 2)
            insights.Add($"You're selling older — avg age {myAvgAge:F0} vs {theirAvgAge:F0}.");

        // Breakout signal
        var myBreakouts = mySide.Count(p => p.BreakoutClassification == "Breakout");
        var theirBreakouts = theirSide.Count(p => p.BreakoutClassification == "Breakout");

        if (myBreakouts > theirBreakouts)
            insights.Add($"You're acquiring {myBreakouts} breakout candidate(s).");
        else if (theirBreakouts > myBreakouts)
            insights.Add($"You're trading away {theirBreakouts} breakout candidate(s).");

        // Prime years
        var myPrime = mySide.Sum(p => p.YearsOfPrimeRemaining);
        var theirPrime = theirSide.Sum(p => p.YearsOfPrimeRemaining);

        if (myPrime > theirPrime + 2)
            insights.Add($"More prime years incoming ({myPrime:F0} vs {theirPrime:F0}).");
        else if (theirPrime > myPrime + 2)
            insights.Add($"Trading away prime years ({myPrime:F0} vs {theirPrime:F0}).");

        return insights;
    }
}
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

public class DfvCalculationService(
    ICareerSimulationRepository careerSimRepository,
    IDynastyValuationRepository valuationRepository,
    ILogger<DfvCalculationService> logger) : IDfvCalculationService
{
    // Annual discount rates by position — RBs depreciate fastest
    private static readonly Dictionary<string, double> DiscountRates = new()
    {
        ["QB"] = 0.10,
        ["RB"] = 0.20,
        ["WR"] = 0.12,
        ["TE"] = 0.13
    };

    // Position scarcity multipliers — QBs and TEs are scarcer in dynasty
    private static readonly Dictionary<string, double> ScarcityMultipliers = new()
    {
        ["QB"] = 0.85,   // QB streaming is common — slight discount
        ["RB"] = 1.10,   // bell cow RBs are scarce
        ["WR"] = 1.00,   // baseline
        ["TE"] = 1.15    // elite TEs are rarest
    };

    public async Task<List<DynastyValuationDocument>> CalculateAllAsync(
        int season, CancellationToken ct = default)
    {
        // Load all existing valuations (have breakout scores from PBI-032)
        var valuations = new List<DynastyValuationDocument>();
        foreach (var pos in new[] { "QB", "RB", "WR", "TE" })
        {
            var posValuations = await valuationRepository.GetByPositionAsync(pos, ct);
            valuations.AddRange(posValuations);
        }

        if (valuations.Count == 0)
        {
            logger.LogWarning("No dynasty valuations found — run breakout detection first");
            return [];
        }

        // Calculate raw DFV for each player
        var rawDfvMap = new Dictionary<string, double>();

        // ── Rookie DFV floor — prevent top prospects from normalizing to bottom ──
        // Career sims for unplayed rookies underestimate true dynasty value.
        // Apply a floor based on breakout score so top prospects surface correctly.
        foreach (var valuation in valuations.Where(v => v.YearsExperience == 0))
        {
            if (!rawDfvMap.TryGetValue(valuation.SleeperPlayerId, out var raw)) continue;

            // For rookies, blend raw DFV with a breakout-score-derived floor
            // BreakoutScore 60+ → floor equivalent to ~rank 8 veteran (good but not elite)
            // BreakoutScore 40+ → floor equivalent to ~rank 15 (solid prospect)
            // BreakoutScore 20+ → floor equivalent to ~rank 25 (fringe starter)
            var breakoutFloor = valuation.BreakoutScore switch
            {
                >= 60 => 400.0,   // blends into top 8 position range after normalization
                >= 40 => 200.0,   // blends into rank 10-20 range
                >= 20 => 100.0,   // blends into rank 20-30 range
                _ => raw      // no floor — let the sim speak
            };

            rawDfvMap[valuation.SleeperPlayerId] = Math.Max(raw, breakoutFloor);
        }

        // Normalize to 0-100 within each position group
        // Dynasty value is position-relative — a 90 WR vs a 90 RB are both elite at their pos
        NormalizeWithinPositions(valuations, rawDfvMap);

        // Stamp results back onto valuation documents
        foreach (var valuation in valuations)
        {
            if (!rawDfvMap.TryGetValue(valuation.SleeperPlayerId, out var raw)) continue;
            valuation.DiscountedFutureValue = Math.Round(raw, 2);
            valuation.TradeValueComputedAt = DateTime.UtcNow;
        }

        logger.LogInformation("DFV calculated for {Count} players", valuations.Count);
        return valuations;
    }

    public double CalculateRawDfv(CareerSimulationDocument careerSim, string position)
    {
        if (careerSim.YearProjections.Count == 0) return 0;

        var discountRate = DiscountRates.GetValueOrDefault(position, 0.12);
        var scarcity = ScarcityMultipliers.GetValueOrDefault(position, 1.0);

        double dfv = 0;
        foreach (var year in careerSim.YearProjections)
        {
            var yearIndex = year.Year - careerSim.Season;
            var discounted = year.SeasonValue / Math.Pow(1 + discountRate, yearIndex);
            dfv += discounted;
        }

        return dfv * scarcity;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static void NormalizeWithinPositions(
    List<DynastyValuationDocument> valuations,
    Dictionary<string, double> rawDfvMap)
    {
        var positions = new[] { "QB", "RB", "WR", "TE" };

        foreach (var pos in positions)
        {
            var posPlayers = valuations
                .Where(v => v.Position == pos
                         && rawDfvMap.ContainsKey(v.SleeperPlayerId)
                         && rawDfvMap[v.SleeperPlayerId] > 0)
                .OrderByDescending(v => rawDfvMap[v.SleeperPlayerId])
                .ToList();

            if (posPlayers.Count == 0) continue;

            // Rank-based normalization — spreads values across the full range
            // Rank 1 = 95, rank 10 = 75, rank 25 = 50, remainder scales to floor of 5
            for (int i = 0; i < posPlayers.Count; i++)
            {
                var rank = i + 1;   // 1-based
                double normalizedValue;

                if (rank == 1)
                    normalizedValue = 95.0;
                else if (rank <= 10)
                    normalizedValue = 95.0 - ((rank - 1) * (20.0 / 9.0));   // 95 → 75
                else if (rank <= 25)
                    normalizedValue = 75.0 - ((rank - 10) * (25.0 / 15.0)); // 75 → 50
                else
                    normalizedValue = Math.Max(5.0, 50.0 - ((rank - 25) * 1.5)); // 50 → floor 5

                rawDfvMap[posPlayers[i].SleeperPlayerId] = Math.Round(normalizedValue, 2);
            }

            // Zero out players with no raw value — keep them at 0, not ranked
            var zeroPlayers = valuations
                .Where(v => v.Position == pos
                         && rawDfvMap.ContainsKey(v.SleeperPlayerId)
                         && rawDfvMap[v.SleeperPlayerId] == 0);

            foreach (var v in zeroPlayers)
                rawDfvMap[v.SleeperPlayerId] = 0;
        }

        // Write normalized values back as TradeValue
        foreach (var valuation in valuations)
        {
            if (rawDfvMap.TryGetValue(valuation.SleeperPlayerId, out var normalized))
                valuation.TradeValue = normalized;
        }
    }
}
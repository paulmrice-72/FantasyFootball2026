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

        foreach (var valuation in valuations)
        {
            if (string.IsNullOrEmpty(valuation.SleeperPlayerId)) continue;

            // FA QBs = out-of-league backups — zero value, skip DB call
            if (valuation.Position == "QB" && string.IsNullOrEmpty(valuation.NflTeam))
            {
                rawDfvMap[valuation.SleeperPlayerId] = 0;
                continue;
            }

            var careerSim = await careerSimRepository
                .GetByPlayerIdAsync(valuation.SleeperPlayerId, ct);

            if (careerSim is null)
            {
                rawDfvMap[valuation.SleeperPlayerId] = 0;
                continue;
            }

            var raw = CalculateRawDfv(careerSim, valuation.Position);
            var breakoutBoost = 1.0 + (valuation.BreakoutScore / 100.0) * 0.25;
            rawDfvMap[valuation.SleeperPlayerId] = raw * breakoutBoost;
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
                         && rawDfvMap.ContainsKey(v.SleeperPlayerId))
                .ToList();

            if (posPlayers.Count == 0) continue;

            var maxRaw = posPlayers.Max(v => rawDfvMap[v.SleeperPlayerId]);
            if (maxRaw <= 0) continue;

            foreach (var v in posPlayers)
            {
                // Normalize to 0-100, apply soft cap so top player = ~95
                var normalized = rawDfvMap[v.SleeperPlayerId] / maxRaw * 95.0;
                rawDfvMap[v.SleeperPlayerId] = Math.Round(normalized, 2);
            }
        }

        // Write normalized values back as TradeValue
        foreach (var valuation in valuations)
        {
            if (rawDfvMap.TryGetValue(valuation.SleeperPlayerId, out var normalized))
                valuation.TradeValue = normalized;
        }
    }
}
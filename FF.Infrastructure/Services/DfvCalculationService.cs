using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

public class DfvCalculationService(
    ICareerSimulationRepository careerSimRepository,
    IDynastyValuationRepository valuationRepository,
    IFantasyProsRookieRankingRepository fpRookieRepository,
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

    // Position scarcity multipliers
    private static readonly Dictionary<string, double> ScarcityMultipliers = new()
    {
        ["QB"] = 0.85, // QB streaming is common — slight discount
        ["RB"] = 1.10, // bell cow RBs are scarce
        ["WR"] = 1.00, // baseline
        ["TE"] = 1.15  // elite TEs are rarest
    };

    public async Task<List<DynastyValuationDocument>> CalculateAllAsync(
        int season,
        CancellationToken ct = default)
    {
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

        // Load FP rookie rankings for this season — used to floor rookie raw DFV
        var fpRookieRankings = await fpRookieRepository.GetAllBySeasonAsync(season, ct);
        var fpRankMap = fpRookieRankings
            .Where(r => r.SleeperPlayerId is not null)
            .ToDictionary(r => r.SleeperPlayerId!, r => r.FantasyProsRank);

        // ── Build raw DFV for every player ─────────────────────────────────
        var rawDfvMap = new Dictionary<string, double>();

        foreach (var valuation in valuations)
        {
            if (string.IsNullOrEmpty(valuation.SleeperPlayerId)) continue;

            if (valuation.Position == "QB" && string.IsNullOrEmpty(valuation.NflTeam))
            {
                rawDfvMap[valuation.SleeperPlayerId] = 0;
                continue;
            }

            // "FA" is a valid pre-draft state — only truly empty NflTeam is penalized
            var isFaSkillPlayer = string.IsNullOrEmpty(valuation.NflTeam)
                                  && valuation.Position != "QB";

            var careerSim = await careerSimRepository
                .GetByPlayerIdAsync(valuation.SleeperPlayerId, ct);

            if (careerSim is null)
            {
                rawDfvMap[valuation.SleeperPlayerId] = 0;
                continue;
            }

            var raw = CalculateRawDfv(careerSim, valuation.Position);
            var breakoutBoost = 1.0 + (valuation.BreakoutScore / 100.0) * 0.25;
            var faPenalty = isFaSkillPlayer ? 0.60 : 1.0;
            rawDfvMap[valuation.SleeperPlayerId] = raw * breakoutBoost * faPenalty;
        }

        foreach (var valuation in valuations.Where(v => v.YearsExperience == 0))
        {
            if (!rawDfvMap.TryGetValue(valuation.SleeperPlayerId, out var raw)) continue;

            // Only apply rookie floor to consensus prospects (must have FP rank)
            // and typical draft age (≤ 23) — excludes older undrafted free agents
            if (!fpRankMap.TryGetValue(valuation.SleeperPlayerId, out var fpRank)) continue;
            if (valuation.Age > 22) continue;

            var rookieFloor = fpRank switch
            {
                <= 5 => 500.0,
                <= 15 => 380.0,
                <= 30 => 250.0,
                <= 50 => 150.0,
                _ => 80.0
            };

            rawDfvMap[valuation.SleeperPlayerId] = Math.Max(raw, rookieFloor);
        }

        // ── Normalize to 0-100 within each position group ──────────────────
        NormalizeWithinPositions(valuations, rawDfvMap);

        // ── Stamp DiscountedFutureValue back onto documents ─────────────────
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

    // ── Private ─────────────────────────────────────────────────────────────────
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

            // Rank-based normalization:
            // Rank 1 = 95, rank 10 = 75, rank 25 = 50, remainder scales to floor of 5
            for (int i = 0; i < posPlayers.Count; i++)
            {
                var rank = i + 1;
                double normalizedValue;

                if (rank == 1)
                    normalizedValue = 95.0;
                else if (rank <= 10)
                    normalizedValue = 95.0 - ((rank - 1) * (20.0 / 9.0)); // 95 → 75
                else if (rank <= 25)
                    normalizedValue = 75.0 - ((rank - 10) * (25.0 / 15.0)); // 75 → 50
                else
                    normalizedValue = Math.Max(5.0, 50.0 - ((rank - 25) * 1.5)); // 50 → floor 5

                rawDfvMap[posPlayers[i].SleeperPlayerId] = Math.Round(normalizedValue, 2);
            }

            // Zero out players with no raw value
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
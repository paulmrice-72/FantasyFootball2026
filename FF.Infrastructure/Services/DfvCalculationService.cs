using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Domain.Enums;
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

    // Standard (1-QB) scarcity multipliers
    // TE premium trimmed: elite scarcity exists at top 2-3 only, not position-wide
    private static readonly Dictionary<string, double> StandardMultipliers = new()
    {
        ["QB"] = 0.85,
        ["RB"] = 1.10,
        ["WR"] = 1.00,
        ["TE"] = 1.05
    };

    private static double GetSuperflexScarcityMultiplier(
    string position,
    DynastyValuationDocument valuation,
    Dictionary<string, double> rawDfvMap)
    {
        if (position != "QB")
        {
            // Non-QB superflex multipliers unchanged
            return position switch
            {
                "RB" => 1.08,
                "WR" => 1.00,
                "TE" => 1.05,
                _ => 1.00
            };
        }

        // QB tier logic — based on CareerValueScore from the career sim.
        // CareerValueScore reflects actual or seeded production:
        //   Proven franchise QB: 900+
        //   Solid starter:       700-899
        //   Fringe starter:      500-699
        //   Backup/rookie:       < 500
        var cvs = valuation.CareerValueScore;
        return cvs switch
        {
            >= 900 => 1.40,   // Elite franchise QB (Allen, Mahomes tier)
            >= 700 => 1.25,   // Solid starter (Burrow, Lawrence, Hurts)
            >= 500 => 1.05,   // Fringe/developing starter
            _ => 0.80    // Backup or unproven rookie — penalize, don't reward
        };
    }

    // Superflex scarcity multipliers — QBs become the most valuable dynasty asset
    private static readonly Dictionary<string, double> SuperflexMultipliers = new()
    {
        ["QB"] = 1.35,  // QB1 = top dynasty asset in superflex
        ["RB"] = 1.08,
        ["WR"] = 1.00,
        ["TE"] = 1.05
    };

    public async Task<List<DynastyValuationDocument>> CalculateAllAsync(
        int season,
        ScoringFormat scoringFormat = ScoringFormat.HalfPpr,
        CancellationToken ct = default)
    {
        var isSuperflexFormat = scoringFormat is ScoringFormat.Superflex or ScoringFormat.SuperflexFullPpr;
        var scarcityMultipliers = isSuperflexFormat ? SuperflexMultipliers : StandardMultipliers;

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

        // Load FP rookie rankings — used to floor rookie raw DFV
        var fpRookieRankings = await fpRookieRepository.GetAllBySeasonAsync(season, ct);
        var fpRankMap = fpRookieRankings
            .Where(r => r.SleeperPlayerId is not null)
            .ToDictionary(r => r.SleeperPlayerId!, r => r.FantasyProsRank);

        // ── Build raw DFV for every player ────────────────────────────────
        var rawDfvMap = new Dictionary<string, double>();

        foreach (var valuation in valuations)
        {
            if (string.IsNullOrEmpty(valuation.SleeperPlayerId)) continue;

            if (valuation.Position == "QB" && string.IsNullOrEmpty(valuation.NflTeam))
            {
                rawDfvMap[valuation.SleeperPlayerId] = 0;
                continue;
            }

            var isFaSkillPlayer = string.IsNullOrEmpty(valuation.NflTeam)
                && valuation.Position != "QB";

            var careerSim = await careerSimRepository
                .GetByPlayerIdAsync(valuation.SleeperPlayerId, ct);

            if (careerSim is null)
            {
                rawDfvMap[valuation.SleeperPlayerId] = 0;
                continue;
            }

            // Get position scarcity multiplier — tiered for QB in superflex
            double scarcity = isSuperflexFormat
                ? GetSuperflexScarcityMultiplier(valuation.Position, valuation, rawDfvMap)
                : scarcityMultipliers.GetValueOrDefault(valuation.Position, 1.0);

            var raw = CalculateRawDfvWithScarcity(careerSim, valuation.Position, scarcity);
            var breakoutBoost = 1.0 + (valuation.BreakoutScore / 100.0) * 0.25;
            var faPenalty = isFaSkillPlayer ? 0.60 : 1.0;
            rawDfvMap[valuation.SleeperPlayerId] = raw * breakoutBoost * faPenalty;
        }

        // ── Rookie floor (unchanged — FP-rank gated) ──────────────────────
        foreach (var valuation in valuations.Where(v => v.YearsExperience == 0))
        {
            if (!rawDfvMap.TryGetValue(valuation.SleeperPlayerId, out var raw)) continue;
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

        // ── Normalize ACROSS all positions (Bug 1 fix) ────────────────────
        NormalizeAcrossAllPositions(valuations, rawDfvMap);

        // ── Stamp results back onto documents ─────────────────────────────
        foreach (var valuation in valuations)
        {
            if (!rawDfvMap.TryGetValue(valuation.SleeperPlayerId, out var normalized)) continue;
            valuation.DiscountedFutureValue = Math.Round(normalized, 2);
            valuation.TradeValue = Math.Round(normalized, 2);
            valuation.ScoringFormat = scoringFormat;
            valuation.TradeValueComputedAt = DateTime.UtcNow;
        }

        logger.LogInformation(
            "DFV calculated for {Count} players — Format: {Format}",
            valuations.Count, scoringFormat);

        return valuations;
    }
    private static double CalculateRawDfvWithScarcity(
    CareerSimulationDocument careerSim,
    string position,
    double scarcity)
    {
        if (careerSim.YearProjections.Count == 0) return 0;

        var discountRate = DiscountRates.GetValueOrDefault(position, 0.12);

        double dfv = 0;
        foreach (var year in careerSim.YearProjections)
        {
            var yearIndex = year.Year - careerSim.Season;
            var discounted = year.SeasonValue / Math.Pow(1 + discountRate, yearIndex);
            dfv += discounted;
        }

        return dfv * scarcity;
    }
    public double CalculateRawDfv(
        CareerSimulationDocument careerSim,
        string position,
        ScoringFormat scoringFormat = ScoringFormat.HalfPpr)
    {
        if (careerSim.YearProjections.Count == 0) return 0;

        var isSuperflexFormat = scoringFormat is ScoringFormat.Superflex or ScoringFormat.SuperflexFullPpr;
        var multipliers = isSuperflexFormat ? SuperflexMultipliers : StandardMultipliers;

        var discountRate = DiscountRates.GetValueOrDefault(position, 0.12);
        var scarcity = multipliers.GetValueOrDefault(position, 1.0);

        double dfv = 0;
        foreach (var year in careerSim.YearProjections)
        {
            var yearIndex = year.Year - careerSim.Season;
            var discounted = year.SeasonValue / Math.Pow(1 + discountRate, yearIndex);
            dfv += discounted;
        }

        return dfv * scarcity;
    }

    // ── Private ────────────────────────────────────────────────────────────

    /// <summary>
    /// Normalizes all players across all positions to a single 0-100 scale.
    /// This preserves cross-position signal: a TE with a weaker career sim
    /// than a comparably ranked WR will score lower, as it should.
    /// TradeValue is also written here for downstream consumers.
    /// </summary>
    private static void NormalizeAcrossAllPositions(
     List<DynastyValuationDocument> valuations,
     Dictionary<string, double> rawDfvMap)
    {
        var eligible = valuations
            .Where(v => rawDfvMap.ContainsKey(v.SleeperPlayerId)
                        && rawDfvMap[v.SleeperPlayerId] > 0)
            .ToList();

        if (eligible.Count == 0) return;

        double maxRaw = eligible.Max(v => rawDfvMap[v.SleeperPlayerId]);
        if (maxRaw == 0) return;

        // Scale proportionally to the best player = 95.
        // This preserves the full signal: a player with half the raw DFV
        // of the best player scores ~47.5, not an arbitrary rank-based number.
        foreach (var v in eligible)
        {
            var scaled = (rawDfvMap[v.SleeperPlayerId] / maxRaw) * 95.0;
            rawDfvMap[v.SleeperPlayerId] = Math.Round(scaled, 2);
        }

        // Zero out players with no raw value
        foreach (var v in valuations)
        {
            if (rawDfvMap.TryGetValue(v.SleeperPlayerId, out var val) && val == 0)
                rawDfvMap[v.SleeperPlayerId] = 0;
        }

        // Write TradeValue for downstream consumers
        foreach (var valuation in valuations)
        {
            if (rawDfvMap.TryGetValue(valuation.SleeperPlayerId, out var normalized))
                valuation.TradeValue = normalized;
        }
    }
}
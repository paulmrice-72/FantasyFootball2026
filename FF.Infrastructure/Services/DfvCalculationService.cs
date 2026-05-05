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
    private static readonly Dictionary<string, double> StandardMultipliers = new()
    {
        ["QB"] = 0.85,
        ["RB"] = 1.10,
        ["WR"] = 1.00,
        ["TE"] = 1.05
    };

    // Superflex scarcity multipliers — fallback for non-QB positions
    private static readonly Dictionary<string, double> SuperflexMultipliers = new()
    {
        ["QB"] = 1.00, // overridden by tiered logic below
        ["RB"] = 1.08,
        ["WR"] = 1.00,
        ["TE"] = 1.05
    };

    private static double GetSuperflexScarcityMultiplier(
        string position,
        DynastyValuationDocument valuation)
    {
        if (position != "QB")
        {
            return position switch
            {
                "RB" => 1.08,
                "WR" => 1.00,
                "TE" => 1.05,
                _ => 1.00
            };
        }

        // QB CVS runs ~40% higher than skill positions due to volume scoring.
        // Deflate before tier lookup so thresholds are comparable across positions.
        //
        // Adjusted CVS reference points (raw CVS / 1.4):
        // Josh Allen  1262 → 901  ≥ 850: top tier
        // Burrow      1192 → 851  ≥ 850: top tier
        // Hurts       1155 → 825  ≥ 700: solid starter
        // Mahomes      925 → 661  ≥ 650: good starter
        // Herbert      923 → 659  ≥ 650: good starter
        // Purdy        965 → 689  ≥ 650: good starter
        // Mayfield    1071 → 765  ≥ 700: solid starter (but age 31 → penalty applies)
        // Goff         948 → 677  ≥ 650: good starter (but age 31 → penalty applies)
        // Darnold      998 → 713  ≥ 700: solid starter (but volatile history)
        var adjustedCvs = valuation.CareerValueScore / 1.4;

        // Age penalty for QBs 30+ — dynasty value compresses past peak age (29).
        // Does not affect Allen (29), Burrow (29), Hurts (27), Mahomes (30 → 0.93).
        // Hits Mayfield (31 → 0.88), Goff (31 → 0.88), older vets (32+ → 0.82).
        var agePenalty = valuation.Age switch
        {
            >= 32 => 0.82,
            31 => 0.88,
            30 => 0.93,
            _ => 1.00
        };

        var baseTier = adjustedCvs switch
        {
            >= 850 => 1.15, // Generational franchise QB
            >= 700 => 1.00, // Solid starter — neutral
            >= 650 => 0.92, // Good starter — slight discount
            >= 550 => 0.84, // Average starter — meaningful discount
            _ => 0.75  // Fringe/backup — penalize
        };

        return baseTier * agePenalty;
    }

    public async Task<List<DynastyValuationDocument>> CalculateAllAsync(
        int season,
        ScoringFormat scoringFormat = ScoringFormat.HalfPpr,
        CancellationToken ct = default)
    {
        var isSuperflexFormat = scoringFormat is ScoringFormat.Superflex or ScoringFormat.SuperflexFullPpr;
        var scarcityMultipliers = isSuperflexFormat ? SuperflexMultipliers : StandardMultipliers;

        // ── Load all valuations ────────────────────────────────────────────
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

        // ── Bulk-load career sims in ONE query ─────────────────────────────
        var allSims = await careerSimRepository.GetAllBySeasonAsync(season, ct);
        var simMap = allSims.ToDictionary(s => s.SleeperPlayerId, s => s);

        logger.LogInformation(
            "Bulk-loaded {Count} career sims for season {Season}", simMap.Count, season);

        // ── Load FP rookie rankings for post-norm floor ────────────────────
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

            if (!simMap.TryGetValue(valuation.SleeperPlayerId, out var careerSim))
            {
                rawDfvMap[valuation.SleeperPlayerId] = 0;
                continue;
            }

            double scarcity = isSuperflexFormat
                ? GetSuperflexScarcityMultiplier(valuation.Position, valuation)
                : scarcityMultipliers.GetValueOrDefault(valuation.Position, 1.0);

            var raw = CalculateRawDfvWithScarcity(careerSim, valuation.Position, scarcity);

            var breakoutBoost = 1.0 + (valuation.BreakoutScore / 100.0) * 0.25;

            // ── Tiered veteran production floor ────────────────────────────
            // Proven producers score low on breakout because the signal targets
            // ascending players, not established elites. Apply a tiered floor
            // based on CVS so Jefferson/Lamb/Chase aren't buried.
            //
            // CVS ≥ 800: elite WR1/RB1 — Jefferson (805), Ja'Marr Chase (1032)
            // CVS ≥ 600: solid starter — AJ Brown, Tee Higgins, Amon-Ra
            if (valuation.BreakoutScore < 25)
            {
                double veteranFloor = valuation.CareerValueScore switch
                {
                    >= 800 => 1.28,
                    >= 600 => 1.18,
                    _ => 1.0
                };
                breakoutBoost = Math.Max(breakoutBoost, veteranFloor);
            }

            var faPenalty = isFaSkillPlayer ? 0.60 : 1.0;
            rawDfvMap[valuation.SleeperPlayerId] = raw * breakoutBoost * faPenalty;
        }

        // ── Normalize ACROSS all positions to 0-95 ────────────────────────
        NormalizeAcrossAllPositions(valuations, rawDfvMap, ceiling: 95.0);

        // ── Rookie floor applied POST-normalization ────────────────────────
        foreach (var valuation in valuations.Where(v => (v.YearsExperience ?? -1) == 0))
        {
            if (!rawDfvMap.TryGetValue(valuation.SleeperPlayerId, out var normalized)) continue;
            if (!fpRankMap.TryGetValue(valuation.SleeperPlayerId, out var fpRank)) continue;
            if (valuation.Age > 22) continue;

            var floorTradeValue = fpRank switch
            {
                1 => 97.0,
                <= 3 => 93.0,
                <= 5 => 88.0,
                <= 10 => 82.0,
                <= 20 => 75.0,
                <= 30 => 65.0,
                <= 50 => 50.0,
                _ => 35.0
            };

            rawDfvMap[valuation.SleeperPlayerId] = Math.Max(normalized, floorTradeValue);
        }

        // ── Final stamp — runs AFTER rookie floor ──────────────────────────
        foreach (var valuation in valuations)
        {
            if (!rawDfvMap.TryGetValue(valuation.SleeperPlayerId, out var final)) continue;
            valuation.DiscountedFutureValue = Math.Round(final, 2);
            valuation.TradeValue = Math.Round(final, 2);
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

    // ── Private ───────────────────────────────────────────────────────────────

    private static void NormalizeAcrossAllPositions(
        List<DynastyValuationDocument> valuations,
        Dictionary<string, double> rawDfvMap,
        double ceiling = 95.0)
    {
        var eligible = valuations
            .Where(v => rawDfvMap.ContainsKey(v.SleeperPlayerId)
                     && rawDfvMap[v.SleeperPlayerId] > 0)
            .ToList();

        if (eligible.Count == 0) return;

        double maxRaw = eligible.Max(v => rawDfvMap[v.SleeperPlayerId]);
        if (maxRaw == 0) return;

        foreach (var v in eligible)
        {
            var scaled = (rawDfvMap[v.SleeperPlayerId] / maxRaw) * ceiling;
            rawDfvMap[v.SleeperPlayerId] = Math.Round(scaled, 2);
        }

        foreach (var v in valuations)
        {
            if (rawDfvMap.TryGetValue(v.SleeperPlayerId, out var val) && val == 0)
                rawDfvMap[v.SleeperPlayerId] = 0;
        }
    }
}
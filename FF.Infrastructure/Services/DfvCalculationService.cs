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

    // FAN-52: TE positional TV caps by rank within the TE pool.
    //
    // Root cause: 216 TEs with NflTeam all get career sims seeded to ~9 FPPG
    // from the position prior (shrinkage blending). They generate similar raw
    // DFV values, and the linear normalization spreads them into TV 40-53.
    // Threshold gates alone don't help for year 2-3 TEs who have actual sim
    // data producing baseFppg > 8.5 — they still pass through.
    //
    // Caps enforce that only genuine TE1s reach WR/RB-equivalent TV ranges.
    // Calibrated against FantasyPros dynasty superflex (May 2026):
    //   TE #1  (McBride)          → cap 65
    //   TE #2  (Bowers)           → cap 55
    //   TE #3–4 (LaPorta/Kraft)   → cap 48
    //   TE #5–8 (Pitts/Kincaid)   → cap 42
    //   TE #9–12 (emerging)       → cap 36
    //   TE #13+ (depth)           → cap 28
    private static readonly double[] TeRankCaps =
    [
        65.0,  // rank 1
        55.0,  // rank 2
        48.0,  // rank 3
        48.0,  // rank 4
        42.0,  // rank 5
        42.0,  // rank 6
        42.0,  // rank 7
        42.0,  // rank 8
        36.0,  // rank 9
        36.0,  // rank 10
        36.0,  // rank 11
        36.0,  // rank 12
        // rank 13+: 28.0
    ];

    private static double GetTeRankCap(int oneBasedRank) =>
        oneBasedRank <= TeRankCaps.Length
            ? TeRankCaps[oneBasedRank - 1]
            : 28.0;

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

        var adjustedCvs = valuation.CareerValueScore / 1.4;
        return adjustedCvs switch
        {
            >= 850 => 1.15,
            >= 700 => 1.00,
            >= 650 => 0.92,
            >= 550 => 0.84,
            _ => 0.75
        };
    }

    public async Task<List<DynastyValuationDocument>> CalculateAllAsync(
        int season,
        ScoringFormat scoringFormat = ScoringFormat.HalfPpr,
        CancellationToken ct = default)
    {
        var isSuperflexFormat = scoringFormat is ScoringFormat.Superflex or ScoringFormat.SuperflexFullPpr;
        var scarcityMultipliers = isSuperflexFormat ? SuperflexMultipliers : StandardMultipliers;

        // ── Load all valuations ──────────────────────────────────────────────
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

        // ── Bulk-load career sims ────────────────────────────────────────────
        var allSims = await careerSimRepository.GetAllBySeasonAsync(season, ct);
        var simMap = allSims
            .GroupBy(s => s.SleeperPlayerId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.ComputedAt).First());

        logger.LogInformation(
            "Bulk-loaded {Count} career sims for season {Season}",
            simMap.Count, season);

        // ── Load FP rookie rankings for post-norm floor ──────────────────────
        var fpRookieRankings = await fpRookieRepository.GetAllBySeasonAsync(season, ct);
        var fpRankMap = fpRookieRankings
            .Where(r => r.SleeperPlayerId is not null)
            .GroupBy(r => r.SleeperPlayerId!)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.FantasyProsRank).First().FantasyProsRank);

        // ── Build raw DFV for every player ──────────────────────────────────
        var rawDfvMap = new Dictionary<string, double>();
        foreach (var valuation in valuations)
        {
            if (string.IsNullOrEmpty(valuation.SleeperPlayerId)) continue;

            // FAN-52: FA zeroing for all positions (QB already zeroed before).
            // Skill position FAs generate phantom DFV from the career sim prior.
            // Exception: rookies with FP rank may not yet have team stamped.
            if (string.IsNullOrEmpty(valuation.NflTeam))
            {
                if (valuation.Position == "QB"
                    || !fpRankMap.ContainsKey(valuation.SleeperPlayerId))
                {
                    rawDfvMap[valuation.SleeperPlayerId] = 0;
                    continue;
                }
            }

            var isFaSkillPlayer = string.IsNullOrEmpty(valuation.NflTeam)
                && valuation.Position != "QB";

            if (!simMap.TryGetValue(valuation.SleeperPlayerId, out var careerSim))
            {
                rawDfvMap[valuation.SleeperPlayerId] = 0;
                continue;
            }

            // Depth gate — year 0-1 unranked players with sub-starter projections
            // FAN-52: TE threshold raised from 6.0 → 9.0
            if (valuation.Position != "QB"
                && (valuation.YearsExperience ?? -1) <= 1
                && !fpRankMap.ContainsKey(valuation.SleeperPlayerId)
                && careerSim.YearProjections.All(y => y.MedianFppg < StarterThresholdDfv(valuation.Position)))
            {
                rawDfvMap[valuation.SleeperPlayerId] = 0;
                continue;
            }

            double scarcity = isSuperflexFormat
                ? GetSuperflexScarcityMultiplier(valuation.Position, valuation)
                : scarcityMultipliers.GetValueOrDefault(valuation.Position, 1.0);

            var raw = CalculateRawDfvWithScarcity(careerSim, valuation.Position, scarcity);

            var ascentBonus = valuation.BreakoutScore >= 50
                ? ((valuation.BreakoutScore - 50.0) / 50.0) * 8.0
                : 0.0;

            var faPenalty = isFaSkillPlayer ? 0.60 : 1.0;
            rawDfvMap[valuation.SleeperPlayerId] = (raw + ascentBonus) * faPenalty;
        }

        var top20Raw = rawDfvMap
            .OrderByDescending(kvp => kvp.Value)
            .Take(20)
            .Select(kvp => $"{kvp.Key}: {kvp.Value:F1}")
            .ToList();
        logger.LogInformation("Top 20 raw DFV before normalization: {Values}",
            string.Join(", ", top20Raw));

        // ── Normalize ACROSS all positions to 0-95 ──────────────────────────
        NormalizeAcrossAllPositions(valuations, rawDfvMap, ceiling: 95.0);

        // ── FAN-52: TE positional rank caps — applied POST-normalization ─────
        // Rank all TEs by normalized score, apply TeRankCaps[].
        // This is the primary fix for the "mid-tier TE inflation" problem.
        // Year 2-3 TEs with real sim data bypass the depth gate (they have
        // legit-looking baseFppg from partial/fill-in game production), so the
        // caps are the enforcement layer that maps them to realistic TV ranges.
        var tesByNormScore = valuations
            .Where(v => v.Position == "TE"
                     && rawDfvMap.TryGetValue(v.SleeperPlayerId, out var s) && s > 0)
            .OrderByDescending(v => rawDfvMap[v.SleeperPlayerId])
            .ToList();

        for (int i = 0; i < tesByNormScore.Count; i++)
        {
            var te = tesByNormScore[i];
            var cap = GetTeRankCap(i + 1);
            if (rawDfvMap.TryGetValue(te.SleeperPlayerId, out var current) && current > cap)
            {
                rawDfvMap[te.SleeperPlayerId] = cap;
                logger.LogDebug(
                    "TE rank cap: {Player} rank {Rank} {Old:F1} → {Cap:F1}",
                    te.PlayerName, i + 1, current, cap);
            }
        }

        // ── Rookie floor — POST-normalization, POST-cap ──────────────────────
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

        // ── Final stamp ──────────────────────────────────────────────────────
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

    // ── Private ──────────────────────────────────────────────────────────────

    private static void NormalizeAcrossAllPositions(
        List<DynastyValuationDocument> valuations,
        Dictionary<string, double> rawDfvMap,
        double ceiling = 95.0)
    {
        var eligible = valuations
            .Where(v => rawDfvMap.ContainsKey(v.SleeperPlayerId)
                     && rawDfvMap[v.SleeperPlayerId] > 0)
            .OrderByDescending(v => rawDfvMap[v.SleeperPlayerId])
            .ToList();

        if (eligible.Count == 0) return;

        double maxRaw = rawDfvMap[eligible[0].SleeperPlayerId];
        double minRaw = rawDfvMap[eligible[^1].SleeperPlayerId];
        double rawRange = maxRaw - minRaw;

        if (rawRange == 0) return;

        foreach (var v in eligible)
        {
            var raw = rawDfvMap[v.SleeperPlayerId];
            var normalized = 2.0 + (raw - minRaw) / rawRange * (ceiling - 2.0);
            rawDfvMap[v.SleeperPlayerId] = Math.Round(normalized, 2);
        }
    }

    // FAN-52: TE raised from 6.0 → 9.0.
    // Catches year 0-1 TEs with no real production. Year 2-3 TEs with actual
    // sim data still bypass this gate — they are handled by TeRankCaps above.
    private static double StarterThresholdDfv(string position) => position switch
    {
        "QB" => 16.0,
        "RB" => 7.0,
        "WR" => 7.5,
        "TE" => 9.0,
        _ => 7.0
    };
}
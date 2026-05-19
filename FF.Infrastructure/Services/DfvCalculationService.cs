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

    // ── P2: Rank-based normalization ──────────────────────────────────────
    // 0.6 (convex): top tier clusters closer together, mid-tier spreads more.
    // Per scoring math reference doc (FAN-62).
    private const double NormExponent = 0.6;
    private const double NormCeiling = 95.0;

    // ── Positional guardrail caps ─────────────────────────────────────────
    // These are GUARDRAILS, not rankings. They prevent gross outliers but
    // do NOT predetermine ordering. The model decides who is QB #1 vs #5 —
    // these just say "no TE should ever score above 70" and "no QB outside
    // the top-6 raw should exceed 80".
    //
    // IMPORTANT: These are tier-based, not rank-by-rank. Multiple players
    // can land in the same tier. The model's raw ordering is preserved
    // within each tier.
    private static double GetQbGuardrailCap(int posRank) => posRank switch
    {
        <= 6 => NormCeiling,  // elite tier — model decides ordering freely
        <= 12 => 80.0,         // solid starters — model orders within band
        <= 20 => 55.0,         // fringe starters / high-upside backups
        <= 30 => 35.0,         // roster QBs
        _ => 15.0          // depth / speculative
    };

    private static double GetTeGuardrailCap(int posRank) => posRank switch
    {
        <= 2 => 70.0,         // elite tier (Bowers/McBride class)
        <= 5 => 55.0,         // strong starters
        <= 10 => 42.0,         // mid-tier
        <= 15 => 32.0,         // back-end starters
        _ => 22.0          // depth
    };

    // ── FP dynasty rank → blending weight ─────────────────────────────────
    // Instead of a floor table that overrides the model, we use FP dynasty
    // rank as a BLENDING signal. Players with stale sim data get pulled
    // toward their FP consensus value proportionally to how stale the data is.
    //
    // The blend weight is based on how far the model's value is below what
    // FP consensus suggests. If the model already agrees, no adjustment.
    // This replaces the old GetDynastyRankFloor() table entirely.
    private static double GetFpDynastyAnchor(int fpRank) => fpRank switch
    {
        <= 5 => 90.0,
        <= 10 => 80.0,
        <= 20 => 70.0,
        <= 30 => 62.0,
        <= 50 => 52.0,
        <= 75 => 42.0,
        <= 100 => 32.0,
        <= 150 => 20.0,
        _ => 0.0
    };

    // ── Superflex QB scarcity ─────────────────────────────────────────────
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

        // ── Load all valuations ──────────────────────────────────────────
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

        // ── Bulk-load career sims ────────────────────────────────────────
        var allSims = await careerSimRepository.GetAllBySeasonAsync(season, ct);
        var simMap = allSims
            .GroupBy(s => s.SleeperPlayerId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.ComputedAt).First());

        logger.LogInformation(
            "Bulk-loaded {Count} career sims for season {Season}",
            simMap.Count, season);

        // ── Load FP rookie rankings ──────────────────────────────────────
        var fpRookieRankings = await fpRookieRepository.GetAllBySeasonAndTypeAsync(season, "Rookie", ct);
        var fpRookieRankMap = fpRookieRankings
            .Where(r => r.SleeperPlayerId is not null)
            .GroupBy(r => r.SleeperPlayerId!)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.FantasyProsRank).First().FantasyProsRank);

        // ── Load FP dynasty rankings ─────────────────────────────────────
        // Used as a blending signal for players with stale/missing sim data.
        var fpDynastyRankings = await fpRookieRepository.GetAllBySeasonAndTypeAsync(season, "Dynasty", ct);
        var fpDynastyRankMap = fpDynastyRankings
            .Where(r => r.SleeperPlayerId is not null && !string.IsNullOrEmpty(r.SleeperPlayerId))
            .GroupBy(r => r.SleeperPlayerId!)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.FantasyProsRank).First().FantasyProsRank);

        logger.LogInformation(
            "Loaded {RookieCount} FP rookie ranks, {DynastyCount} FP dynasty ranks",
            fpRookieRankMap.Count, fpDynastyRankMap.Count);

        // ── Build raw DFV for every player ───────────────────────────────
        var rawDfvMap = new Dictionary<string, double>();
        foreach (var valuation in valuations)
        {
            if (string.IsNullOrEmpty(valuation.SleeperPlayerId)) continue;

            // FA zeroing — skill position FAs generate phantom DFV from the career sim prior.
            // Exception: rookies with FP rank may not yet have team stamped.
            if (string.IsNullOrEmpty(valuation.NflTeam))
            {
                if (valuation.Position == "QB"
                    || !fpRookieRankMap.ContainsKey(valuation.SleeperPlayerId))
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

            // Depth gate — year 0-1 unranked players with sub-starter projections.
            if (valuation.Position != "QB"
                && (valuation.YearsExperience ?? -1) <= 1
                && !fpRookieRankMap.ContainsKey(valuation.SleeperPlayerId)
                && careerSim.YearProjections.All(y => y.MedianFppg < StarterThresholdDfv(valuation.Position)))
            {
                rawDfvMap[valuation.SleeperPlayerId] = 0;
                continue;
            }

            double scarcity = isSuperflexFormat
                ? GetSuperflexScarcityMultiplier(valuation.Position, valuation)
                : scarcityMultipliers.GetValueOrDefault(valuation.Position, 1.0);

            var raw = CalculateRawDfvWithScarcity(careerSim, valuation.Position, scarcity);

            // P3: Ascent bonus — additive, only for genuine breakout candidates.
            // Per scoring math reference (FAN-63): threshold 50, max +8 raw points.
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

        // ── P2: Rank-based power curve normalization ─────────────────────
        // Per scoring math reference (FAN-62): sort by raw DFV descending,
        // assign finalScore = ceiling * (1 - (rank-1)/(N-1))^exponent.
        // Top player always scores ~95. Stable — adding one player shifts
        // others by ≤1 rank.
        NormalizeAcrossAllPositions(valuations, rawDfvMap, NormCeiling);

        // ── FP dynasty consensus blend — POST-normalize ──────────────────
        // For players whose model value is significantly below their FP
        // dynasty consensus, blend upward. This handles players with stale
        // or missing sim data (no 2025 nflverse yet) without overriding
        // the model entirely like the old floor table did.
        //
        // Blend formula: if model < anchor, new = model + (anchor - model) * blendWeight
        // blendWeight = 0.65 — trusts FP consensus moderately but lets the
        // model retain influence. Players already at or above their anchor
        // are untouched.
        const double fpBlendWeight = 0.65;
        foreach (var valuation in valuations)
        {
            if (string.IsNullOrEmpty(valuation.SleeperPlayerId)) continue;
            if (!fpDynastyRankMap.TryGetValue(valuation.SleeperPlayerId, out var dynastyRank)) continue;
            if (!rawDfvMap.TryGetValue(valuation.SleeperPlayerId, out var current)) continue;

            var anchor = GetFpDynastyAnchor(dynastyRank);
            if (anchor <= 0 || current >= anchor) continue;

            var blended = current + (anchor - current) * fpBlendWeight;
            rawDfvMap[valuation.SleeperPlayerId] = Math.Round(blended, 2);

            logger.LogDebug(
                "FP dynasty blend: {Player} ({Position}) FP rank {Rank} anchor {Anchor:F0} — {Old:F1} → {New:F1}",
                valuation.PlayerName, valuation.Position, dynastyRank, anchor, current, blended);
        }

        // ── Positional guardrail caps — POST-blend ───────────────────────
        // These are tier-based guardrails, NOT predetermined rankings.
        // They prevent gross positional outliers but preserve the model's
        // ordering within each tier. A QB who the model ranks #8 stays at
        // #8 — the cap just prevents them from scoring above 80.
        ApplyPositionalGuardrails(
            valuations, rawDfvMap,
            position: "QB",
            getCap: GetQbGuardrailCap,
            logLabel: "QB guardrail");

        ApplyPositionalGuardrails(
            valuations, rawDfvMap,
            position: "TE",
            getCap: GetTeGuardrailCap,
            logLabel: "TE guardrail");

        // ── Rookie floor — POST-guardrails ────────────────────────────────
        // Catches rookies not yet in FP dynasty rankings (recent draftees).
        // Uses FP rookie rank as a floor — never lowers, only raises.
        // TE rookies are gated separately to prevent them from exceeding
        // the TE guardrail ceiling.
        foreach (var valuation in valuations.Where(v => (v.YearsExperience ?? -1) == 0))
        {
            if (!rawDfvMap.TryGetValue(valuation.SleeperPlayerId, out var normalized)) continue;
            if (!fpRookieRankMap.TryGetValue(valuation.SleeperPlayerId, out var fpRank)) continue;
            if (valuation.Age > 22) continue;

            double floorTradeValue;

            if (valuation.Position == "TE")
            {
                // TE rookie floors capped well below TE guardrail ceiling (70)
                // so they can't leap-frog established TE1s.
                floorTradeValue = fpRank switch
                {
                    <= 5 => 45.0,
                    <= 15 => 38.0,
                    <= 30 => 30.0,
                    _ => 20.0
                };
            }
            else
            {
                floorTradeValue = fpRank switch
                {
                    1 => 92.0,
                    <= 3 => 88.0,
                    <= 5 => 83.0,
                    <= 10 => 76.0,
                    <= 20 => 68.0,
                    <= 30 => 58.0,
                    <= 50 => 45.0,
                    _ => 30.0
                };
            }

            rawDfvMap[valuation.SleeperPlayerId] = Math.Max(normalized, floorTradeValue);
        }

        // ── Final stamp ──────────────────────────────────────────────────
        foreach (var valuation in valuations)
        {
            if (!rawDfvMap.TryGetValue(valuation.SleeperPlayerId, out var final)) continue;
            valuation.DiscountedFutureValue = Math.Round(final, 2);
            valuation.TradeValue = Math.Round(final, 2);
            valuation.ScoringFormat = scoringFormat;
            valuation.TradeValueComputedAt = DateTime.UtcNow;
        }

        // ── Log final top-30 for diagnostics ─────────────────────────────
        var top30Final = valuations
            .Where(v => v.TradeValue > 0)
            .OrderByDescending(v => v.TradeValue)
            .Take(30)
            .Select((v, i) => $"#{i + 1} {v.PlayerName} ({v.Position}) TV={v.TradeValue:F1}")
            .ToList();
        logger.LogInformation("Final top 30: {Rankings}", string.Join(" | ", top30Final));

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

    /// <summary>
    /// P2: Rank-based power curve normalization (FAN-62).
    /// Sort all players with raw > 0 by raw DFV descending.
    /// Top player scores ~ceiling; distribution controlled by NormExponent.
    /// Stable: adding/removing one player shifts others by ≤1 rank.
    /// </summary>
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

        int n = eligible.Count;

    /// <summary>
    /// Applies tier-based guardrail caps to a position.
    /// Unlike the old rank-by-rank cap arrays, these use broad tiers
    /// (top 6, 7-12, 13-20, etc.) so the model's ordering within a tier
    /// is preserved. Caps only fire when a player's value exceeds the
    /// tier ceiling — they never raise values.
    /// </summary>
    private void ApplyPositionalGuardrails(
        List<DynastyValuationDocument> valuations,
        Dictionary<string, double> rawDfvMap,
        string position,
        Func<int, double> getCap,
        string logLabel)
    {
        var ranked = valuations
            .Where(v => v.Position == position
                        && rawDfvMap.TryGetValue(v.SleeperPlayerId, out var s) && s > 0)
            .OrderByDescending(v => rawDfvMap[v.SleeperPlayerId])
            .ToList();

        for (int i = 0; i < ranked.Count; i++)
        {
            var player = ranked[i];
            var cap = getCap(i + 1);
            if (rawDfvMap.TryGetValue(player.SleeperPlayerId, out var current) && current > cap)
            {
                rawDfvMap[player.SleeperPlayerId] = cap;
                logger.LogDebug(
                    "{Label}: {Player} rank {Rank} {Old:F1} → {Cap:F1}",
                    logLabel, player.PlayerName, i + 1, current, cap);
            }
        }
    }

    private static double StarterThresholdDfv(string position) => position switch
    {
        "QB" => 16.0,
        "RB" => 7.0,
        "WR" => 7.5,
        "TE" => 9.0,
        _ => 7.0
    };
}
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

    // P2 normalization exponent — controls distribution shape.
    // 0.6 (convex): top tier clusters closer together, mid-tier spreads more.
    private const double NormExponent = 0.6;

    // QB positional TV caps by dynasty rank — applied POST-normalization, POST-floor.
    // Caps are the final enforcement layer — they always win over floors.
    // Pipeline order: normalize → dynasty floor → QB caps → TE caps → rookie floor
    // This means: FP consensus lifts stale players (Daniels/Love/Fields), then caps
    // push win-now QBs (Purdy/Goff/Mayfield) back down regardless of their FP rank.
    // Rank 26+: 5.0
    private static readonly double[] QbRankCaps =
    [
        95.0, // rank 1  — Allen
        91.0, // rank 2  — Hurts
        88.0, // rank 3  — Jackson
        85.0, // rank 4  — Burrow
        83.0, // rank 5  — Mahomes
        80.0, // rank 6  — Herbert
        78.0, // rank 7  — Lawrence
        76.0, // rank 8  — Love (young, upside)
        74.0, // rank 9  — Fields (young, upside)
        72.0, // rank 10 — Nix (2026 starter, youth)
        68.0, // rank 11 — Mendoza (2026 rookie QB1, elite landing spot)
        58.0, // rank 12 — Stroud / Daniels tier
        52.0, // rank 13
        48.0, // rank 14
        44.0, // rank 15
        35.0, // rank 16 — Purdy / Goff / Mayfield — win-now, no dynasty upside
        30.0, // rank 17
        26.0, // rank 18
        22.0, // rank 19
        18.0, // rank 20
        14.0, // rank 21 — clear backups
        12.0, // rank 22
        10.0, // rank 23
        8.0,  // rank 24
        6.0,  // rank 25
        // rank 26+: 5.0
    ];

    private static double GetQbRankCap(int oneBasedRank) =>
        oneBasedRank <= QbRankCaps.Length
            ? QbRankCaps[oneBasedRank - 1]
            : 5.0;

    // TE positional TV caps by rank — applied POST-floor.
    // Rank 13+: 28.0
    private static readonly double[] TeRankCaps =
    [
        68.0, // rank 1  — Bowers
        65.0, // rank 2  — McBride
        52.0, // rank 3  — Loveland / LaPorta
        48.0, // rank 4  — LaPorta / Kraft
        42.0, // rank 5
        42.0, // rank 6
        42.0, // rank 7
        42.0, // rank 8
        36.0, // rank 9
        36.0, // rank 10
        36.0, // rank 11
        36.0, // rank 12
        // rank 13+: 28.0
    ];

    private static double GetTeRankCap(int oneBasedRank) =>
        oneBasedRank <= TeRankCaps.Length
            ? TeRankCaps[oneBasedRank - 1]
            : 28.0;

    // FP dynasty overall rank → TV floor.
    // Applied POST-normalization but PRE-positional-caps so that caps always win.
    //
    // Purpose: players whose career sim is stale/missing (no 2025 data) get lifted
    // to their FP community consensus value before caps enforce the ceiling.
    //
    // Example flow for Purdy (FP dynasty rank ~38, cap rank ~16):
    //   P2 normalize  → ~62 (QB #4 in raw pool)
    //   Dynasty floor → Math.Max(62, 58) = 62 (floor doesn't fire — already above)
    //   QB caps       → rank 16 cap = 35 → Purdy stamped at 35 ✓
    //
    // Example flow for Jayden Daniels (FP dynasty rank ~7, no sim data):
    //   P2 normalize  → 0 (zeroed — no sim, no team)
    //   Dynasty floor → Math.Max(0, 83) = 83
    //   QB caps       → rank 12 cap = 58 → Daniels stamped at 58 ✓
    //
    // Rank 151+: no floor (depth players — speculative)
    private static double GetDynastyRankFloor(int overallRank) => overallRank switch
    {
        <= 5 => 90.0,
        <= 10 => 83.0,
        <= 20 => 76.0,
        <= 30 => 68.0,
        <= 50 => 58.0,
        <= 75 => 45.0,
        <= 100 => 35.0,
        <= 150 => 22.0,
        _ => 0.0
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

        // ── Load FP rookie rankings ──────────────────────────────────────────
        var fpRookieRankings = await fpRookieRepository.GetAllBySeasonAndTypeAsync(season, "Rookie", ct);
        var fpRookieRankMap = fpRookieRankings
            .Where(r => r.SleeperPlayerId is not null)
            .GroupBy(r => r.SleeperPlayerId!)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.FantasyProsRank).First().FantasyProsRank);

        // ── Load FP dynasty rankings ─────────────────────────────────────────
        // Used as a pre-cap floor signal for veterans with stale/missing sim data.
        var fpDynastyRankings = await fpRookieRepository.GetAllBySeasonAndTypeAsync(season, "Dynasty", ct);
        var fpDynastyRankMap = fpDynastyRankings
            .Where(r => r.SleeperPlayerId is not null && !string.IsNullOrEmpty(r.SleeperPlayerId))
            .GroupBy(r => r.SleeperPlayerId!)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.FantasyProsRank).First().FantasyProsRank);

        logger.LogInformation(
            "Loaded {RookieCount} FP rookie ranks, {DynastyCount} FP dynasty ranks",
            fpRookieRankMap.Count, fpDynastyRankMap.Count);

        // ── Build raw DFV for every player ───────────────────────────────────
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

        // ── P2: Rank-based power curve normalization ─────────────────────────
        NormalizeAcrossAllPositions(valuations, rawDfvMap, ceiling: 95.0);

        // ── FP dynasty floor — POST-normalize, PRE-caps ──────────────────────
        // Caps run after this block so they always win. Floor only raises (Math.Max).
        // Players with no dynasty rank entry are unaffected.
        // Players already above their floor (good sim data) are unaffected.
        // FA players with TradeValue=0 who have a dynasty rank get lifted (e.g.
        // a vet QB who lost their team but FP still ranks — let caps handle zeroing).
        foreach (var valuation in valuations)
        {
            if (string.IsNullOrEmpty(valuation.SleeperPlayerId)) continue;
            if (!fpDynastyRankMap.TryGetValue(valuation.SleeperPlayerId, out var dynastyRank)) continue;

            var floor = GetDynastyRankFloor(dynastyRank);
            if (floor <= 0) continue;

            if (!rawDfvMap.TryGetValue(valuation.SleeperPlayerId, out var current)) continue;
            if (floor <= current) continue; // already above floor — sim data is good

            rawDfvMap[valuation.SleeperPlayerId] = floor;
            logger.LogDebug(
                "Dynasty floor: {Player} ({Position}) FP rank {Rank} {Old:F1} → {Floor:F1}",
                valuation.PlayerName, valuation.Position, dynastyRank, current, floor);
        }

        // ── QB positional rank caps — POST-floor ─────────────────────────────
        // Caps are the final word on QB values. Win-now QBs (Purdy/Goff/Mayfield)
        // may have been lifted by dynasty floor; caps push them back down.
        ApplyPositionalRankCaps(
            valuations, rawDfvMap,
            position: "QB",
            getCap: GetQbRankCap,
            logLabel: "QB rank cap");

        // ── TE positional rank caps — POST-floor ─────────────────────────────
        ApplyPositionalRankCaps(
            valuations, rawDfvMap,
            position: "TE",
            getCap: GetTeRankCap,
            logLabel: "TE rank cap");

        // ── Rookie floor — POST-cap ───────────────────────────────────────────
        // Most high-value rookies are already in the dynasty import and got a floor
        // above. This catches any rookie not yet in the dynasty rankings (very recent
        // draftees added after the last FP dynasty export).
        foreach (var valuation in valuations.Where(v => (v.YearsExperience ?? -1) == 0))
        {
            if (!rawDfvMap.TryGetValue(valuation.SleeperPlayerId, out var normalized)) continue;
            if (!fpRookieRankMap.TryGetValue(valuation.SleeperPlayerId, out var fpRank)) continue;
            if (valuation.Age > 22) continue;

            double floorTradeValue;

            if (valuation.Position == "TE")
            {
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

        int n = eligible.Count;

        for (int i = 0; i < n; i++)
        {
            var id = eligible[i].SleeperPlayerId;
            double rankFraction = n > 1 ? (double)i / (n - 1) : 0.0;
            double normalized = ceiling * Math.Pow(1.0 - rankFraction, NormExponent);
            rawDfvMap[id] = Math.Round(normalized, 2);
        }
    }

    private void ApplyPositionalRankCaps(
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
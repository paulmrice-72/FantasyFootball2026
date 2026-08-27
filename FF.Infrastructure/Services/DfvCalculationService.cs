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

    // ── FP dynasty blend weight ─────────────────────────────────────────────
    // Fix 2026-08-27b: TE-scoped bump. Kraft/LaPorta/McBride/Bowers stayed
    // overvalued (Δ-22 to Δ-58) even after the symmetric blend fix, because
    // their post-normalize raw values cluster tightly (~90 each — real
    // trailing FPPG barely separates them) while FP dynasty consensus prices
    // in scheme/role-security signal the model doesn't have, spreading them
    // rank 18 to 73+. A 0.65 blend only closes 65% of that gap. Scoped to TE
    // only (not the global weight) per Paul's call — raising it league-wide
    // would also reshuffle QB/RB/WR blending, which isn't the problem here.
    // Tunable via the calibration harness; verify live before widening scope.
    private const double DefaultFpBlendWeight = 0.65;
    private const double TeFpBlendWeight = 0.85;

    // Headroom above a TE's FP dynasty anchor before the guardrail cap fires.
    // Small and deliberate: the anchor already IS the market signal for a
    // ranked TE, so the tier cap (92/89/84/78...) should defer to it instead
    // of the other way around. See GetTeEffectiveCap below.
    private const double TeAnchorCapHeadroom = 2.0;

    // ── P2: Rank-based normalization ──────────────────────────────────────
    // P2 normalization exponent — controls distribution shape.
    // 0.9 (slightly convex): good spread across the full range while
    // preserving mild top-tier compression. At 0.6 (original), the top 200
    // were crammed into a 75-95 band, burying TE caps in a sea of WRs/QBs.
    // 0.9 gives rank 100 ≈ 80.7 and rank 150 ≈ 73.4 with N=600, so a TE
    // capped at 78 lands around overall rank 115 instead of rank 170.
    // Tunable via calibration harness (FAN-62).
    private const double NormExponent = 0.9;
    private const double NormCeiling = 95.0;

    // ── Positional guardrail caps ─────────────────────────────────────────
    // These are GUARDRAILS, not rankings. They prevent gross outliers but
    // do NOT predetermine ordering. The model decides who is QB #1 vs #5 —
    // these just say "no TE should ever score above 70" and "no QB outside
    // the top-3 raw should exceed 85".
    //
    // IMPORTANT: These are tier-based, not rank-by-rank. Multiple players
    // can land in the same tier. The model's raw ordering is preserved
    // within each tier.
    //
    // FAN-95 (2026-08-25): the old "<=6 => NormCeiling" band let ANY six QBs
    // by raw rank plateau at ~95 with almost no spread between them (94.5 to
    // 95.0 across all six). Real Superflex market consensus (FantasyPros)
    // doesn't do this — it clusters a true top tier (Allen/Lamar/Burrow) up
    // near the top of the whole board, then drops the next tier (Hurts/
    // Purdy/Nix) down a full level, not a fraction of a point. Split the old
    // 6-player "free" band into a true top-3 (still uncapped — the model
    // decides that ordering freely) and a 4-6 band with a real, lower cap
    // (85) so that separation can show up in TradeValue instead of getting
    // flattened. Tunable via the calibration harness like the rest of this
    // tier table.
    private static double GetQbGuardrailCap(int posRank) => posRank switch
    {
        <= 3 => NormCeiling,  // true elite tier — model decides ordering freely
        <= 6 => 85.0,          // very good starters — real tier below elite, not a plateau
        <= 12 => 80.0,         // solid starters — model orders within band
        <= 20 => 55.0,         // fringe starters / high-upside backups
        <= 30 => 35.0,         // roster QBs
        _ => 15.0          // depth / speculative
    };

    private static double GetTeGuardrailCap(int posRank) => posRank switch
    {
        1 => 92.0,         // generational TE — overall ~25 (Bowers class)
        2 => 89.0,         // elite TE1 — overall ~40 (McBride class)
        <= 4 => 84.0,         // strong starters — overall ~75 (LaPorta/Kraft tier)
        <= 8 => 78.0,         // mid-tier starters — overall ~120
        <= 12 => 70.0,         // back-end starters — overall ~175
        _ => 50.0          // depth
    };

    // FAN-95 (2026-08-25): WR had NO guardrail at all — QB and TE were the
    // only capped positions. With no cap, P3 ascent bonus + raw CVS let
    // several boom/bust or hype-driven WRs (Rice, Higgins, Jameson Williams,
    // Pickens, Wilson) sit at 90-92 TradeValue despite FP dynasty consensus
    // ranking them 36-67 overall — nothing in the pipeline pulls an
    // overvalued player back down (the FP blend only raises undervalued
    // players, never lowers overvalued ones). Same tier-based approach as
    // QB/TE: caps compress the ceiling per band, model still orders freely
    // within a band. Tiers are wider than QB/TE since the startable WR pool
    // is much deeper. Tunable via the calibration harness.
    //
    // FAN-95 addendum (same day): the first pass's flat "<=8 => 87" band
    // reproduced the exact plateau bug the QB fix was meant to kill — five
    // WRs (London, Smith-Njigba, Rice, Collins, Higgins) all landed at an
    // identical 87.0 because their pre-cap values all exceeded a single flat
    // ceiling for the whole 4-8 band, burying a legitimately elite player
    // (Smith-Njigba, FP rank 7) next to clear outliers (Rice FP 55,
    // Higgins FP 53). Split 4-8 into a narrower 4-5 and 6-8 band, same
    // remedy as the QB 1-6 split, so the model's within-tier ordering has
    // somewhere to show up instead of collapsing to one number.
    private static double GetWrGuardrailCap(int posRank) => posRank switch
    {
        <= 3 => NormCeiling,  // true elite — unquestioned WR1 overall tier
        <= 5 => 89.0,          // near-elite — real tier below the top-3, not a plateau
        <= 8 => 83.0,          // clear WR1 tier
        <= 16 => 76.0,          // strong starters
        <= 28 => 65.0,          // solid WR2/flex
        <= 45 => 50.0,          // streaming / flex depth
        <= 70 => 35.0,          // bench
        _ => 20.0          // deep bench / speculative
    };

    // Fix 2026-08-27 (live calibration): RB had NO guardrail at all — the
    // same gap FAN-95 already found and fixed for WR ("nothing pulls an
    // overvalued player back down"), just never extended to RB. Confirmed
    // live: 7 of the top 20 in a calibration run were RBs, every one
    // overvalued relative to FP (Gibbs Δ-6 through Breece Hall Δ-26).
    // Ceiling starts below WR/QB's uncapped 95 — FP's own dynasty consensus
    // never puts even the best RB above ~rank 11 overall, so the cap should
    // reflect the market's structural discount on RB career length instead
    // of letting RB compete for the very top of the board. Same tier-based,
    // model-orders-within-tier mechanism as QB/WR/TE. Initial pass — tunable
    // via the calibration harness like the rest of this tier table.
    private static double GetRbGuardrailCap(int posRank) => posRank switch
    {
        <= 2 => 88.0,
        <= 5 => 82.0,
        <= 8 => 74.0,
        <= 15 => 64.0,
        <= 25 => 52.0,
        <= 40 => 38.0,
        <= 60 => 25.0,
        _ => 15.0
    };

    // ── FP dynasty rank → blend anchor ─────────────────────────────────────
    // Used as a blending signal for players whose model value is below their
    // FP dynasty consensus. Only raises, never lowers.
    //
    // Anchors are aligned with the P2 curve (0.9 exponent, N≈600), offset
    // ~3 points below to let the model retain some influence. At FP rank 18
    // (Bowers), P2 produces ~92.6 — anchor is 90, so blend can pull him
    // into the right neighborhood. Without this alignment, the old anchors
    // (70 for rank ≤20) were 22+ points below the P2 curve, making the
    // blend ineffective at correcting stale-data players.
    //
    // Blend formula (in CalculateAllAsync): if model < anchor,
    //   new = model + (anchor - model) * 0.65
    // Players already above their anchor are untouched.
    // Fix 2026-08-27 (live calibration — J.J. McCarthy, FP dynasty rank 219):
    // anchors used to stop at rank 200 (anchor 0.0 = blend inapplicable beyond
    // that). That left the pipeline with NO way to correct a model value that
    // disagrees with the market past rank 200 — confirmed live for McCarthy,
    // whose career sim is grounded in real 2025 production data (legitimate,
    // not a bug) but whose TV (83.8) sat nowhere near his real FP dynasty
    // consensus rank (219) because the blend simply never engaged. Guardrail
    // caps alone can't fix this since they're keyed to the model's own
    // internal rank order, not FP rank. Extended the taper down to the tail
    // of the P2 curve (N≈600) so deep-ranked players can still be pulled
    // toward consensus, just with a smaller anchor the further out they are.
    private static double GetFpDynastyAnchor(int fpRank) => fpRank switch
    {
        <= 5 => 92.0,    // P2 rank 5  ≈ 94.4
        <= 10 => 91.0,    // P2 rank 10 ≈ 93.7
        <= 20 => 90.0,    // P2 rank 20 ≈ 92.3
        <= 30 => 88.0,    // P2 rank 30 ≈ 90.5
        <= 50 => 85.0,    // P2 rank 50 ≈ 88.0
        <= 75 => 80.0,    // P2 rank 75 ≈ 84.4
        <= 100 => 74.0,    // P2 rank 100 ≈ 80.7
        <= 150 => 62.0,    // P2 rank 150 ≈ 73.4
        <= 200 => 50.0,    // P2 rank 200 ≈ 66.1
        <= 300 => 35.0,    // P2 rank 300 ≈ 51.0
        <= 450 => 20.0,    // P2 rank 450 ≈ 27.3
        _ => 10.0    // deep bench / effectively unranked — still correctable
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
        var fpRookieRankings = await fpRookieRepository.GetAllBySeasonAndTypeAsync(season, "Rookie", ct)
                               ?? Array.Empty<FantasyProsRookieRankingDocument>();
        var fpRookieRankMap = fpRookieRankings
            .Where(r => r.SleeperPlayerId is not null)
            .GroupBy(r => r.SleeperPlayerId!)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.FantasyProsRank).First().FantasyProsRank);

        // ── Load FP dynasty rankings ─────────────────────────────────────
        // Used as a blending signal for players with stale/missing sim data.
        var fpDynastyRankings = await fpRookieRepository.GetAllBySeasonAndTypeAsync(season, "Dynasty", ct)
                                ?? Array.Empty<FantasyProsRookieRankingDocument>();
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
        // Blends every dynasty-ranked player's model value toward their FP
        // consensus anchor — in BOTH directions.
        //
        // Fix 2026-08-26 (live calibration + Mongo data pull): this used to
        // only raise undervalued players (`current >= anchor` skipped
        // everyone else), on the theory that it existed to handle stale/
        // missing sim data. But that leaves the model with NO way to correct
        // an OVERvalued player — confirmed live for Trey McBride, Tucker
        // Kraft, Sam LaPorta, and Brock Bowers: two years of season-average
        // FPPG only shows a modest gap between them (9.35 → 13.33), but FP's
        // dynasty consensus sees a much wider one (LaPorta #73 to McBride
        // #37) because it prices in things trailing-FPPG-only inputs can't
        // see (target competition, scheme, role security). Every one of
        // those four had already cleared their FP anchor pre-blend, so the
        // one-directional blend never engaged for any of them, and they hit
        // the guardrail ceiling with nothing upstream having corrected them.
        // Same root cause FAN-95 already flagged for WR ("nothing pulls an
        // overvalued player back down").
        //
        // Blend formula (symmetric): new = model + (anchor - model) * blendWeight.
        // When model < anchor, this raises (unchanged from before). When
        // model > anchor, (anchor - model) is negative and this now pulls
        // the value DOWN toward consensus by the same weight instead
        // of leaving it untouched. Players already exactly at their anchor
        // are (trivially) unaffected either way.
        //
        // Fix 2026-08-27b: TE gets a higher blend weight (see
        // TeFpBlendWeight above) — every other position keeps 0.65.
        foreach (var valuation in valuations)
        {
            if (string.IsNullOrEmpty(valuation.SleeperPlayerId)) continue;
            if (!fpDynastyRankMap.TryGetValue(valuation.SleeperPlayerId, out var dynastyRank)) continue;
            if (!rawDfvMap.TryGetValue(valuation.SleeperPlayerId, out var current)) continue;

            var anchor = GetFpDynastyAnchor(dynastyRank);
            if (anchor <= 0 || current == anchor) continue;

            var fpBlendWeight = valuation.Position == "TE" ? TeFpBlendWeight : DefaultFpBlendWeight;
            var blended = current + (anchor - current) * fpBlendWeight;
            rawDfvMap[valuation.SleeperPlayerId] = Math.Round(blended, 2);

            logger.LogDebug(
                "FP dynasty blend: {Player} ({Position}) FP rank {Rank} anchor {Anchor:F0} — {Old:F1} → {New:F1} ({Direction})",
                valuation.PlayerName, valuation.Position, dynastyRank, anchor, current, blended,
                blended > current ? "raised" : "lowered");
        }

        // ── Positional guardrail caps — POST-blend ───────────────────────
        // These are tier-based guardrails, NOT predetermined rankings.
        // They prevent gross positional outliers but preserve the model's
        // ordering within each tier. A QB who the model ranks #8 stays at
        // #8 — the cap just prevents them from scoring above 80.
        ApplyPositionalGuardrails(
            valuations, rawDfvMap,
            position: "QB",
            getCap: (rank, _) => GetQbGuardrailCap(rank),
            logLabel: "QB guardrail");

        // ── TE guardrails — POST-blend ──────────────────────────────────
        // Fix 2026-08-27b: the flat tier cap (92/89/84/78...) was never the
        // binding constraint for Kraft/LaPorta/McBride — their post-blend
        // values sat comfortably UNDER it, so the "guardrail" wasn't
        // actually guarding anything for this group. Reconciled with the FP
        // anchor: when a TE has a real FP dynasty rank, its effective cap is
        // min(tier cap, anchor + headroom) instead of the tier cap alone —
        // the anchor already IS the market signal, so it should bind first.
        // TEs with no FP dynasty rank (too new to be priced in) fall back to
        // the plain tier cap, same as before.
        double GetTeEffectiveCap(int posRank, DynastyValuationDocument v)
        {
            var tierCap = GetTeGuardrailCap(posRank);
            if (string.IsNullOrEmpty(v.SleeperPlayerId)
                || !fpDynastyRankMap.TryGetValue(v.SleeperPlayerId, out var fpRank))
            {
                return tierCap;
            }

            var anchor = GetFpDynastyAnchor(fpRank);
            return anchor > 0 ? Math.Min(tierCap, anchor + TeAnchorCapHeadroom) : tierCap;
        }

        ApplyPositionalGuardrails(
            valuations, rawDfvMap,
            position: "TE",
            getCap: GetTeEffectiveCap,
            logLabel: "TE guardrail");

        // ── WR guardrails — POST-blend ──────────────────────────────────
        // FAN-95: new guardrail — WR previously had no cap at all, letting
        // ascent-bonus/CVS-driven outliers plateau near the ceiling with no
        // downward correction available anywhere else in the pipeline.
        ApplyPositionalGuardrails(
            valuations, rawDfvMap,
            position: "WR",
            getCap: (rank, _) => GetWrGuardrailCap(rank),
            logLabel: "WR guardrail");

        // ── RB guardrails — POST-blend ──────────────────────────────────
        // Fix 2026-08-27: RB was the last unguarded position — same
        // "nothing pulls an overvalued player back down" gap FAN-95 already
        // fixed for WR.
        ApplyPositionalGuardrails(
            valuations, rawDfvMap,
            position: "RB",
            getCap: (rank, _) => GetRbGuardrailCap(rank),
            logLabel: "RB guardrail");

        // ── Rookie floor — POST-guardrails ────────────────────────────────
        // Catches rookies NOT YET in FP dynasty rankings (very recent
        // draftees the dynasty consensus hasn't priced in yet) using their
        // FP ROOKIE-class rank as a stand-in floor. Never lowers, only
        // raises — for THAT case.
        //
        // Bug found 2026-08-26 (live calibration run): this fired
        // unconditionally for ANY YearsExperience==0 player with an FP
        // rookie rank, even when the player ALREADY has a real FP DYNASTY
        // rank — i.e. even when the dynasty blend earlier in this method
        // already placed them correctly against the full market. Because
        // this runs last and is a plain Math.Max, the rookie-rank floor
        // then overrode that good, market-accurate value. Confirmed live:
        // Fernando Mendoza (FP rookie top-3 → floor 88) has an FP DYNASTY
        // rank of #50 (anchor ~45 from the blend above) but still landed
        // at TV 88 (Δ-35 vs FP). Carnell Tate: FP dynasty #42 (anchor ~85)
        // but rookie-floored to 88 anyway (Δ-26) — in his case the floor
        // happened to roughly agree, Mendoza's shows how badly it can
        // disagree. Being the #2-3 ranked ROOKIE doesn't mean top-3
        // overall value, and once a real dynasty rank exists, it's a
        // strictly better signal than the rookie-class rank.
        //
        // Fix: skip this fallback entirely for anyone already present in
        // fpDynastyRankMap — the blend step already handled them. This
        // floor now only fires for genuine "too new to be dynasty-ranked
        // yet" players, which is what the comment always said it was for.
        foreach (var valuation in valuations.Where(v => (v.YearsExperience ?? -1) == 0))
        {
            if (fpDynastyRankMap.ContainsKey(valuation.SleeperPlayerId)) continue;
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

        for (int i = 0; i < n; i++)
        {
            var id = eligible[i].SleeperPlayerId;
            double rankFraction = n > 1 ? (double)i / (n - 1) : 0.0;
            double normalized = ceiling * Math.Pow(1.0 - rankFraction, NormExponent);
            rawDfvMap[id] = Math.Round(normalized, 2);
        }
    }

    /// <summary>
    /// Applies tier-based guardrail caps to a position.
    /// Unlike the old rank-by-rank cap arrays, these use broad tiers
    /// (top 6, 7-12, 13-20, etc.) so the model's ordering within a tier
    /// is preserved. Caps only fire when a player's value exceeds the
    /// tier ceiling — they never raise values.
    ///
    /// FAN-95 structural finding (2026-08-25), fixed here: a flat
    /// "clamp to cap" collapses every player who exceeds the ceiling to
    /// the IDENTICAL value — this reproduced three times in one session
    /// (QB, WR, then a pre-existing TE instance: LaPorta Δ-54 and Kraft
    /// Δ-42 both pinned at 84.0). Splitting tiers narrower shrinks the
    /// blast radius but never eliminates it — any two players whose
    /// pre-cap value both exceed a tier's cap still tie.
    ///
    /// Fix: instead of clamping every overflowing player in a tier to
    /// the same cap, spread them across a small band immediately below
    /// the cap, ordered by their pre-cap model value (best player closest
    /// to the cap, weakest furthest below it). This is the same rank-based
    /// spreading idea as P2 normalization, just applied locally within a
    /// guardrail tier instead of globally — it's still a ceiling (nobody
    /// exceeds `cap`), it just stops erasing the model's within-tier
    /// ordering. Band width grows gently with the number of players
    /// being compressed together (more collisions = more room needed to
    /// keep them distinct) but is kept small so it can't bleed into the
    /// next tier down. Tunable via the calibration harness.
    ///
    /// getCap takes (positionRank, player) rather than just positionRank —
    /// most positions ignore the player and return a flat tier cap as
    /// before; TE's cap (2026-08-27b) uses it to fold in the player's own
    /// FP dynasty anchor. When caps vary per player instead of per tier,
    /// "tiers" below naturally shrink to runs of players sharing an
    /// identical cap value (often just one) — the overflow/banding logic
    /// still applies correctly, it just has less to spread across.
    /// </summary>
    private void ApplyPositionalGuardrails(
        List<DynastyValuationDocument> valuations,
        Dictionary<string, double> rawDfvMap,
        string position,
        Func<int, DynastyValuationDocument, double> getCap,
        string logLabel)
    {
        var ranked = valuations
            .Where(v => v.Position == position
                        && rawDfvMap.TryGetValue(v.SleeperPlayerId, out var s) && s > 0)
            .OrderByDescending(v => rawDfvMap[v.SleeperPlayerId])
            .ToList();

        int i = 0;
        while (i < ranked.Count)
        {
            // Walk forward while consecutive ranks share the same cap —
            // that run of players is one guardrail tier.
            var cap = getCap(i + 1, ranked[i]);
            var tierStart = i;
            while (i < ranked.Count && getCap(i + 1, ranked[i]) == cap) i++;
            var tierEnd = i; // exclusive

            // Within this tier, find the players whose pre-cap value actually
            // exceeds the ceiling — only those get compressed.
            var overflow = new List<int>();
            for (var j = tierStart; j < tierEnd; j++)
            {
                if (rawDfvMap.TryGetValue(ranked[j].SleeperPlayerId, out var current) && current > cap)
                    overflow.Add(j);
            }

            if (overflow.Count == 0) continue;

            // Band width: small and driven by how many players are colliding
            // at this cap, capped at 4.0 so it never crosses into the next
            // tier's range.
            var bandWidth = Math.Min(4.0, 0.4 * overflow.Count);

            for (var k = 0; k < overflow.Count; k++)
            {
                var player = ranked[overflow[k]];
                var rankFraction = overflow.Count > 1 ? (double)k / (overflow.Count - 1) : 0.0;
                var compressed = Math.Round(cap - rankFraction * bandWidth, 2);
                var old = rawDfvMap[player.SleeperPlayerId];
                rawDfvMap[player.SleeperPlayerId] = compressed;

                logger.LogDebug(
                    "{Label}: {Player} rank {Rank} {Old:F1} → {New:F1} (tier ceiling {Cap:F1}, {N} compressed together)",
                    logLabel, player.PlayerName, overflow[k] + 1, old, compressed, cap, overflow.Count);
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
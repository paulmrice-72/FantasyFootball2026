using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Application.Services;
using FF.Domain.Documents;
using FF.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

public class DfvCalculationService(
    ICareerSimulationRepository careerSimRepository,
    IDynastyValuationRepository valuationRepository,
    IFantasyProsRookieRankingRepository fpRookieRepository,
    IPositionalValueCurveRepository curveRepository,
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

    // ── FAN-153 phase 2: the positional scarcity multipliers are gone ─────
    // StandardMultipliers (QB 0.85 / RB 1.10 / WR 1.00 / TE 1.05),
    // SuperflexMultipliers, and GetSuperflexScarcityMultiplier — a five-tier
    // step function on CareerValueScore / 1.4 — were deleted here. They were
    // one of the two crude stand-ins for a cross-position basis, and they
    // pulled against the other: pre-cap they put Blake Bortles at RV 74.5 and
    // seventeen quarterbacks in the raw top 20, while the cap tables then
    // pushed real starters the other way (Dak Prescott Δ+154, Jared Goff
    // Δ+130). Neither was calibrated against anything.
    //
    // Value over replacement replaces them. A multiplier scales a point total
    // that was never comparable across positions in the first place; a
    // subtraction makes it comparable. See ValueOverReplacementCalculator.
    //
    // The cap tables are still here, deliberately. Two changes, two numbers:
    // this commit is measured with them in place on the Raw basis — which is
    // snapshotted pre-guardrail and therefore measures VOR alone — and they
    // come out in a commit of their own with its own measurement.

    /// <summary>
    /// What a free agent's <b>projection</b> is discounted by — not his surplus.
    /// Unchanged in value; the placement is the part that matters, and it is
    /// passed into the VOR calculation rather than applied to its result.
    ///
    /// <para>
    /// Two separate reasons, both learned the expensive way on 2026-09-08.
    /// Applied to the surplus, a 0.60 multiplier <i>raises</i> a sub-replacement
    /// player, because his value is negative — a free agent would be promoted up
    /// the board for being a free agent. Guarding that with "only when positive"
    /// fixes the sign and leaves the real defect: 60% of a surplus is a far
    /// smaller correction than 60% of gross production, so every free agent
    /// gained <c>0.4 × K</c>, where K is the position's discounted replacement
    /// constant. RB's K is 238.7, against an RB1 gross of ~581.
    /// </para>
    ///
    /// <para>
    /// This constant was calibrated as a fraction of a player's projected
    /// production — "he has no team, so this projection is too high" — so that is
    /// where it belongs. Inside the subtraction it reproduces its own calibrated
    /// behaviour exactly: the crossover against a rostered player sits at
    /// <c>G' &lt; 0.60G</c>, the same place it sat before value over replacement
    /// existed.
    /// </para>
    /// </summary>
    private const double FreeAgentPenalty = 0.60;

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

    public async Task<List<DynastyValuationDocument>> CalculateAllAsync(
        int season,
        ScoringFormat scoringFormat = ScoringFormat.HalfPpr,
        bool disableVor = false,
        CancellationToken ct = default)
    {
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

        // ── FAN-153 phase 2: the cross-position ladder ───────────────────
        // Replacement level is a property of a league, so the curves are stored
        // and the league-specific half is an index into them. The board is
        // global and therefore has to commit to one shape: the canonical team
        // count, and the roster configuration the scoring format implies. A
        // league that differs resolves its own level from the same curves.
        //
        // Resolved here, before the career simulations are bulk-loaded, so a
        // run with no curves fails on the thing that is actually missing rather
        // than several thousand documents later. Nothing below this point can
        // produce a usable board without it, and the sims are the expensive
        // read in this method.
        //
        // The curve is read under the scoring half of the format only — see
        // ValueOverReplacementCalculator.CurveScoringKey. A superflex league and
        // a 1-QB league on the same scoring read the same stored documents and
        // differ only in where they index into them, which is the design phase 1
        // chose.
        //
        // The control experiment skips this whole block. It does not need the
        // curves, and it must not fail on their absence — the point of the run
        // is to score today's simulations without the subtraction, so a missing
        // curve is not an error on this path.
        IReadOnlyDictionary<(int Year, string Position), PositionalReplacementLevel> replacementLevels =
            new Dictionary<(int Year, string Position), PositionalReplacementLevel>();

        if (disableVor)
        {
            logger.LogWarning(
                "FAN-153 CONTROL RUN — value over replacement is DISABLED. Raw value is the "
                + "plain discounted career total: no replacement subtraction, and no positional "
                + "scarcity multiplier (deleted in phase 2, deliberately not resurrected). "
                + "Within-position ordering is identical to both the 09-08 and phase 2 "
                + "pipelines by construction, so any within-position rho difference belongs to "
                + "the calibration population or to upstream drift. This board is a "
                + "measurement, not a serving state — re-run without the flag afterwards.");
        }
        else
        {
            var curveKey = ValueOverReplacementCalculator.CurveScoringKey(scoringFormat);
            var curves = await curveRepository.GetAllBySeasonAndFormatAsync(season, curveKey, ct);

            if (curves.Count == 0)
                throw new InvalidOperationException(
                    $"No positional value curves found for season {season} under scoring key "
                    + $"'{curveKey}' (requested format {scoringFormat}). Run "
                    + $"POST /api/v1/admin/jobs/build-value-curves for season {season} first — "
                    + "without them there is no replacement level, and valuing players on raw "
                    + "point totals is the cross-position defect FAN-153 exists to remove.");

            var leagueShape = ValueOverReplacementCalculator.LeagueShapeFor(scoringFormat);
            replacementLevels = ValueOverReplacementCalculator.ResolveLevels(
                curves, leagueShape, ValueOverReplacementCalculator.CanonicalTeamCount);

            foreach (var yearLevels in replacementLevels
                         .GroupBy(kv => kv.Key.Year)
                         .OrderBy(g => g.Key))
            {
                logger.LogInformation(
                    "FAN-153 replacement level {Year} at {Teams} teams ({Format}) — {Levels}",
                    yearLevels.Key,
                    ValueOverReplacementCalculator.CanonicalTeamCount,
                    scoringFormat,
                    string.Join(", ", yearLevels
                        .OrderBy(kv => kv.Key.Position)
                        .Select(kv => $"{kv.Key.Position} {kv.Value.Level:F1} at cutoff {kv.Value.Cutoff}"
                                      + (kv.Value.PoolExhausted ? " (pool exhausted)" : ""))));
            }
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

        // FAN-153 phase 2. Which players this run actually scored, kept apart
        // from the values themselves.
        //
        // Until now "scored" and "positive" were the same thing, and every stage
        // downstream filtered on raw > 0. Under value over replacement a player
        // projected below replacement is negative — a real, informative rank,
        // not an absence — so that filter would delete most of the board. It is
        // FAN-170's defect at scale: a low projection read as a missing player.
        //
        // The two guards below (no NFL team, no career simulation) are genuine
        // absences and are the only things that keep a player out of this set.
        // Zeroed keeps meaning absent, and never means bad.
        var scoredIds = new HashSet<string>(StringComparer.Ordinal);
        var yearsWithoutCurve = 0;
        var subReplacementCount = 0;

        // FAN-170: the cohort the deleted year 0-1 depth gate used to zero.
        // Logged rather than acted on — it is the attribution key for any
        // change in a within-position rho on the next calibration run.
        var admittedByGateRemoval = new List<DynastyValuationDocument>();

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

            // FAN-170: the year 0-1 depth gate used to sit here and zero any
            // non-QB with <= 1 year of experience, no FP rookie rank, and no
            // projected season clearing StarterThresholdDfv(position). It is
            // deleted, along with the threshold table itself — the last
            // surviving member of the three-way constant collision FAN-165
            // recorded and FAN-168 removed from CareerSimulationService.
            //
            // Why it had to go, in order of weight:
            //
            // 1. It compared a projection against the prior that produced it.
            //    A year 0-1 player with no FP rookie row is projected almost
            //    entirely from PositionPriors[position]; the gate then asked
            //    whether that projection cleared StarterThresholdDfv(position)
            //    — 9.0 against a 9.0 TE prior. The answer was decided by the
            //    aging multiplier applied to a constant, never by the player.
            // 2. FAN-168 removed the reason it existed. The gate was
            //    compensating for a starter prior being applied to backups;
            //    the prior is role-conditioned now, so a backup is projected
            //    low at source instead of being inflated and then deleted.
            // 3. Ranking a player low and removing him from the board are
            //    different claims. The two guards above this one are genuine
            //    absences of data — no NFL team, no career simulation. This
            //    one was a judgement about a projection that exists, and
            //    because NormalizeAcrossAllPositions only ranks raw > 0, it
            //    also dropped its victims out of the P2 population entirely.
            //    Measured 2026-09-08: Elijah Arroyo at RV 0, TE 91 of 92,
            //    against an FP rank of 243.
            //
            // The Position != "QB" asymmetry went with it; it never had a
            // stated reason.
            //
            // Attribution note for the next calibration run: removing this
            // gate cannot reorder any player who was already being measured.
            // P2 normalization is rank-based, so admitting players lowers
            // every surviving player's rankFraction by the same construction
            // and preserves their relative order exactly. Any delta in a
            // within-position rho is therefore attributable entirely to the
            // newly admitted cohort logged below, and to nothing else.
            //
            // This tracks the gate's POPULATION, not the subset it caught —
            // reproducing the caught subset would mean keeping the threshold
            // table alive to measure a threshold table. Non-QB only, because
            // the gate never fired on QB. Players in here who were already
            // clearing the old threshold are unaffected by this change and
            // are simply carried in the count.
            if (valuation.Position != "QB"
                && (valuation.YearsExperience ?? -1) <= 1
                && !fpRookieRankMap.ContainsKey(valuation.SleeperPlayerId))
            {
                admittedByGateRemoval.Add(valuation);
            }

            // FAN-153 phase 2. The discounted sum of each projected season's
            // surplus over that season's replacement level, in place of the
            // discounted point total times a positional multiplier.
            //
            // Under the control flag the subtraction is skipped and the value is
            // the plain discounted career total, with the free-agent penalty
            // applied to the projection exactly as it was before value over
            // replacement existed — 0.60 x G, the scale the constant was
            // calibrated on. Nothing else on this path differs.
            double baseValue;

            if (disableVor)
            {
                baseValue = CalculateRawDfv(careerSim, valuation.Position, scoringFormat);
                if (isFaSkillPlayer) baseValue *= FreeAgentPenalty;
            }
            else
            {
                var vor = ValueOverReplacementCalculator.Compute(
                    careerSim,
                    valuation.Position,
                    DiscountRates.GetValueOrDefault(valuation.Position, 0.12),
                    replacementLevels,
                    projectionMultiplier: isFaSkillPlayer ? FreeAgentPenalty : 1.0);

                yearsWithoutCurve += vor.YearsWithoutCurve;
                baseValue = vor.Total;
            }

            if (baseValue < 0) subReplacementCount++;

            // P3: Ascent bonus — additive, only for genuine breakout candidates.
            // Per scoring math reference (FAN-63): threshold 50, max +8 raw points.
            //
            // Note the scale it now sits on. It was +8 against a discounted career
            // total in the high hundreds — well under one percent. Against a
            // surplus over replacement it is a materially larger nudge, because
            // the surplus is the small difference between two large numbers.
            // Left at its calibrated value for this measurement rather than
            // re-tuned in the same commit as the change that moved its scale.
            var ascentBonus = valuation.BreakoutScore >= 50
                ? ((valuation.BreakoutScore - 50.0) / 50.0) * 8.0
                : 0.0;

            // The free-agent penalty is applied above, inside the subtraction —
            // see FreeAgentPenalty and ValueOverReplacementCalculator.Compute.
            //
            // The ascent bonus stays additive and stays here. Additive is the one
            // placement that is genuinely indifferent: the replacement level is a
            // per-position constant, so adding 8 before subtracting it and adding
            // 8 after are the same arithmetic. Which is also why the bonus cannot
            // be behind any within-position movement on this run — worth writing
            // down, because it was the other suspect.
            rawDfvMap[valuation.SleeperPlayerId] = baseValue + ascentBonus;
            scoredIds.Add(valuation.SleeperPlayerId);
        }

        // A run where this is non-zero has curves that are stale relative to its
        // simulations: some projected seasons had nothing to price against and
        // were skipped, so those players scored low for a reason that has
        // nothing to do with them.
        if (yearsWithoutCurve > 0)
        {
            logger.LogWarning(
                "FAN-153: {Count} projected seasons had no positional value curve and were "
                + "skipped. Rebuild the curves for season {Season} / {Format} — the players "
                + "affected are undervalued by however many seasons went unpriced.",
                yearsWithoutCurve, season, scoringFormat);
        }

        // On the control path a negative is not possible — a discounted sum of
        // non-negative season values cannot go below zero — so a non-zero count
        // here would mean a projected SeasonValue is itself negative, which is a
        // defect upstream rather than a ranking. Logged as a warning for that
        // reason rather than folded into the same sentence.
        if (disableVor)
        {
            logger.LogInformation(
                "FAN-153 CONTROL: {Scored} players scored on the plain discounted career total.",
                scoredIds.Count);

            if (subReplacementCount > 0)
            {
                logger.LogWarning(
                    "FAN-153 CONTROL: {Count} players scored below zero without a replacement "
                    + "subtraction in play. That requires a negative projected SeasonValue and "
                    + "is an upstream defect, not a ranking.",
                    subReplacementCount);
            }
        }
        else
        {
            logger.LogInformation(
                "FAN-153: {Scored} players scored on value over replacement, {SubReplacement} of them "
                + "below replacement. A negative here is a rank, not a deletion.",
                scoredIds.Count, subReplacementCount);
        }

        // FAN-170: who the deleted depth gate would have removed, and where
        // they actually landed. A large cohort carrying real values is the
        // expected outcome; a large cohort clustered near the top would mean
        // the gate was suppressing phantom value rather than deleting real
        // players, and would point at an absence-based rule to replace it.
        if (admittedByGateRemoval.Count > 0)
        {
            var admittedByPosition = admittedByGateRemoval
                .GroupBy(v => v.Position)
                .OrderBy(g => g.Key)
                .Select(g => $"{g.Key}={g.Count()}");

            logger.LogInformation(
                "FAN-170: {Count} non-QB year 0-1 unranked players scored — the removed depth gate's population, of which the sub-threshold subset used to be zeroed ({ByPosition})",
                admittedByGateRemoval.Count, string.Join(", ", admittedByPosition));

            var admittedTop10 = admittedByGateRemoval
                .Where(v => rawDfvMap.ContainsKey(v.SleeperPlayerId))
                .OrderByDescending(v => rawDfvMap[v.SleeperPlayerId])
                .Take(10)
                .Select(v => $"{v.PlayerName} ({v.Position}) {rawDfvMap[v.SleeperPlayerId]:F1}");

            logger.LogInformation(
                "FAN-170: highest-valued of that cohort — {Players}",
                string.Join(", ", admittedTop10));
        }

        var namesById = valuations
            .Where(v => !string.IsNullOrEmpty(v.SleeperPlayerId))
            .GroupBy(v => v.SleeperPlayerId)
            .ToDictionary(g => g.Key, g => $"{g.First().PlayerName} ({g.First().Position})");

        string Describe(string id, double value) =>
            $"{namesById.GetValueOrDefault(id, id)} {value:F1}";

        var scoredDescending = rawDfvMap
            .Where(kvp => scoredIds.Contains(kvp.Key))
            .OrderByDescending(kvp => kvp.Value)
            .ToList();

        logger.LogInformation("Top 20 value over replacement before normalization: {Values}",
            string.Join(", ", scoredDescending.Take(20).Select(kvp => Describe(kvp.Key, kvp.Value))));

        // The other end, which is the half this change is really about. Under the
        // old scale the bottom of the board was a crowd of small positive numbers
        // that said nothing; under VOR it is an ordering by how far below
        // replacement a player projects.
        logger.LogInformation("Bottom 20 value over replacement before normalization: {Values}",
            string.Join(", ", scoredDescending.TakeLast(20).Select(kvp => Describe(kvp.Key, kvp.Value))));

        // ── P2: Rank-based power curve normalization ─────────────────────
        // Per scoring math reference (FAN-62): sort by raw DFV descending,
        // assign finalScore = ceiling * (1 - (rank-1)/(N-1))^exponent.
        // Top player always scores ~95. Stable — adding one player shifts
        // others by ≤1 rank.
        NormalizeAcrossAllPositions(valuations, rawDfvMap, scoredIds, NormCeiling);

        // ── FAN-159: fork the model's own answer here ────────────────────
        // Everything below this line that touches rawDfvMap is either a
        // FantasyPros-derived correction (the blend, the TE anchor cap, the
        // rookie floor) or one of our own guardrails. modelOnlyMap follows the
        // second set and none of the first, so it ends up as this pipeline's
        // unborrowed opinion.
        //
        // Why this exists: the calibration harness ranked players by TradeValue
        // and scored that ordering against FantasyPros rank — while TradeValue
        // is itself blended 65% toward a FantasyPros-rank anchor. It was
        // grading FantasyPros against itself, and every ρ it has ever produced
        // is inflated by construction. Splitting the value in two lets the
        // product keep serving the blend (a good prior) while the harness
        // measures something the blend has not touched.
        var modelOnlyMap = new Dictionary<string, double>(rawDfvMap);

        // ── FAN-166: fork again, one step earlier ────────────────────────
        // Same snapshot, kept separately and never touched again. modelOnlyMap
        // goes on to receive the positional guardrails; this does not.
        //
        // The reason is that FAN-159's provenance line ("the guardrails are
        // ours, so they stay in") turned out to carry far more weight than the
        // judgement behind it assumed. The cap tables were fitted against the
        // blended distribution, where the FP blend had already corrected each
        // player's positional rank before the caps landed. Applied to the
        // model's own uncorrected rank order they bind somewhere else entirely,
        // and because they partition by position they are the only stage in
        // this pipeline that can reorder players across positions — which is
        // exactly what they do. Measured 2026-09-08: raw DFV has A.J. Brown at
        // 428 and Jeremy Ruckert at 195; ModelValue has Ruckert ahead.
        //
        // Keeping this snapshot costs one dictionary copy and turns "how much
        // of rho is the cap tables" from an argument into a subtraction.
        var rawValueMap = new Dictionary<string, double>(rawDfvMap);

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

        // ── FAN-159: the same guardrails, on the unblended track ─────────
        // The guardrails are ours, so the model-only value gets them too —
        // otherwise "the model" would mean "the model with its known outlier
        // corrections switched off", which is not a thing we ship and not a
        // thing worth measuring.
        //
        // One deliberate difference: TE uses the plain tier cap here, not
        // GetTeEffectiveCap. That cap folds in the player's own FP dynasty
        // anchor (min(tierCap, anchor + headroom)), which is exactly the
        // borrowed signal this track is meant to exclude. Every other position
        // uses the identical cap function as above.
        ApplyPositionalGuardrails(
            valuations, modelOnlyMap,
            position: "QB",
            getCap: (rank, _) => GetQbGuardrailCap(rank),
            logLabel: "QB guardrail (model-only)");

        ApplyPositionalGuardrails(
            valuations, modelOnlyMap,
            position: "TE",
            getCap: (rank, _) => GetTeGuardrailCap(rank),
            logLabel: "TE guardrail (model-only)");

        ApplyPositionalGuardrails(
            valuations, modelOnlyMap,
            position: "WR",
            getCap: (rank, _) => GetWrGuardrailCap(rank),
            logLabel: "WR guardrail (model-only)");

        ApplyPositionalGuardrails(
            valuations, modelOnlyMap,
            position: "RB",
            getCap: (rank, _) => GetRbGuardrailCap(rank),
            logLabel: "RB guardrail (model-only)");

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
            // FAN-175. Stamped before the guard below, not after it. A player this
            // run skipped entirely would otherwise keep whatever flag a previous
            // run left on him, and a stale true is the one value this field must
            // never carry — it is the thing every selection query now trusts.
            valuation.IsScored = !string.IsNullOrEmpty(valuation.SleeperPlayerId)
                                 && scoredIds.Contains(valuation.SleeperPlayerId);

            if (!rawDfvMap.TryGetValue(valuation.SleeperPlayerId, out var final)) continue;
            valuation.DiscountedFutureValue = Math.Round(final, 2);
            valuation.TradeValue = Math.Round(final, 2);
            valuation.ScoringFormat = scoringFormat;
            valuation.TradeValueComputedAt = DateTime.UtcNow;

            // FAN-159. Stamped alongside TradeValue rather than in a pass of its
            // own so the two can never be written from different runs — a
            // calibration comparing this run's model value against a previous
            // run's blended value would be measuring the gap between two
            // pipelines, and would look like a model change.
            valuation.ModelValue = modelOnlyMap.TryGetValue(valuation.SleeperPlayerId, out var modelOnly)
                ? Math.Round(modelOnly, 2)
                : 0.0;

            // FAN-166. Stamped in the same pass for the same reason: three
            // values read from three different runs would be measuring the gaps
            // between three pipelines.
            valuation.RawValue = rawValueMap.TryGetValue(valuation.SleeperPlayerId, out var rawOnly)
                ? Math.Round(rawOnly, 2)
                : 0.0;
        }

        // ── FAN-175: the population any selection is entitled to draw from ────
        // Logged per run so a harness result can be read against the number of
        // players that actually existed to grade. A position whose scored count is
        // below the harness's request is one where the top-N selection used to run
        // off the end of the real data into the zeroed tail: measured 2026-09-10,
        // QB 115, RB 189 and TE 201 against a request for 250, which padded the
        // graded population with 135, 61 and 49 players nobody had valued.
        foreach (var position in new[] { "QB", "RB", "WR", "TE" })
        {
            var total = valuations.Count(v => v.Position == position);
            if (total == 0) continue;

            logger.LogInformation(
                "FAN-175 scored population {Position}: {Scored} of {Total}",
                position, valuations.Count(v => v.Position == position && v.IsScored), total);
        }

        // ── FAN-166: what the guardrails cost, per position, per run ─────
        // Logged rather than inferred. If this line is quiet the caps are not
        // binding; if it is loud, the number the harness reports on the Model
        // basis is substantially this table rather than the model.
        foreach (var pos in new[] { "QB", "RB", "WR", "TE" })
        {
            var affected = valuations
                .Where(v => v.Position == pos
                            && v.RawValue > 0
                            && v.ModelValue < v.RawValue - 0.01)
                .ToList();

            if (affected.Count == 0)
            {
                logger.LogInformation(
                    "Guardrail impact {Position}: no player compressed", pos);
                continue;
            }

            logger.LogInformation(
                "Guardrail impact {Position}: {Count} compressed, mean drop {Mean:F1}, " +
                "max drop {Max:F1} ({Worst} {Raw:F1} → {Model:F1})",
                pos,
                affected.Count,
                affected.Average(v => v.RawValue - v.ModelValue),
                affected.Max(v => v.RawValue - v.ModelValue),
                affected.MaxBy(v => v.RawValue - v.ModelValue)!.PlayerName,
                affected.MaxBy(v => v.RawValue - v.ModelValue)!.RawValue,
                affected.MaxBy(v => v.RawValue - v.ModelValue)!.ModelValue);
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

    /// <summary>
    /// One player's discounted career point total. Single-player, no league
    /// context, and therefore <b>not</b> comparable across positions.
    ///
    /// <para>
    /// FAN-153 phase 2: the positional scarcity multiplier came out of here with
    /// the rest of them. It cannot be replaced by value over replacement in this
    /// signature — VOR needs a league shape and a stored curve, and this method
    /// is handed a single simulation and nothing else. The <c>scoringFormat</c>
    /// parameter is kept for source compatibility and no longer changes the
    /// answer; it was only ever selecting a multiplier table.
    /// </para>
    ///
    /// <para>
    /// Comparing two players at different positions with this number is the
    /// defect FAN-166 measured. Anything that needs a cross-position comparison
    /// wants <see cref="CalculateAllAsync"/>'s output or
    /// <see cref="ValueOverReplacementCalculator"/> directly.
    /// </para>
    /// </summary>
    public double CalculateRawDfv(
        CareerSimulationDocument careerSim,
        string position,
        ScoringFormat scoringFormat = ScoringFormat.HalfPpr)
    {
        if (careerSim.YearProjections.Count == 0) return 0;

        var discountRate = DiscountRates.GetValueOrDefault(position, 0.12);

        double dfv = 0;
        foreach (var year in careerSim.YearProjections)
        {
            var yearIndex = year.Year - careerSim.Season;
            dfv += year.SeasonValue / Math.Pow(1 + discountRate, yearIndex);
        }

        return dfv;
    }

    // ── Private ──────────────────────────────────────────────────────────────

    /// <summary>
    /// P2: Rank-based power curve normalization (FAN-62).
    /// Sorts every scored player by value descending; the top scores ~ceiling and
    /// the distribution is controlled by NormExponent. Stable: adding or removing
    /// one player shifts the others by ≤1 rank.
    ///
    /// <para>
    /// FAN-153 phase 2: the population is now an explicit set of scored players
    /// rather than everyone with a value above zero. Value over replacement is
    /// signed — a player projected below replacement is negative — and a
    /// <c>raw &gt; 0</c> filter would have dropped most of the board out of the
    /// P2 population entirely, which is exactly what the year 0-1 depth gate did
    /// to Elijah Arroyo before FAN-170 removed it, at a few hundred times the
    /// scale.
    /// </para>
    ///
    /// <para>
    /// The rank is what survives this step, not the value, so the negative
    /// numbers do not propagate: everything comes out in [0, ceiling]. Worth
    /// knowing when reading a stamped value: the last-ranked player normalizes
    /// to exactly 0, which is the same number an unscored player carries. A
    /// "zeroed player" check has to exclude the final rank or it reports a
    /// phantom every run.
    /// </para>
    /// </summary>
    private static void NormalizeAcrossAllPositions(
        List<DynastyValuationDocument> valuations,
        Dictionary<string, double> rawDfvMap,
        HashSet<string> scoredIds,
        double ceiling = 95.0)
    {
        var eligible = valuations
            .Where(v => scoredIds.Contains(v.SleeperPlayerId)
                        && rawDfvMap.ContainsKey(v.SleeperPlayerId))
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

    // FAN-170: StarterThresholdDfv (QB 16.0 / RB 7.0 / WR 7.5 / TE 9.0) was
    // removed here along with its only caller. It was the third and last
    // member of the constant collision FAN-165 recorded — one concept
    // ("is this player a starter") expressed as three numbers across two
    // services, with TE's threshold set equal to the TE prior it was
    // implicitly testing. FAN-168 deleted the other two. Do not reintroduce
    // a starter threshold without a replacement-level number behind it.
}
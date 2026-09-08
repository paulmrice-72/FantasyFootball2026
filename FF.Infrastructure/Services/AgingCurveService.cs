using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Domain.Enums;
using FF.Domain.Services;
using MathNet.Numerics;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace FF.Infrastructure.Services;

/// <summary>
/// Builds one aging curve per position from historical game logs.
///
/// <para>
/// FAN-157 — this used to pool every game log by the player's age at the time
/// and average within each age bucket. That measures <em>who is on the field at
/// each age</em>, not <em>how a player changes as he ages</em>. The 21-22
/// buckets are mostly rookies and low-usage backups; the 27-29 buckets are
/// almost entirely established starters, because the players who were not good
/// enough are out of the league by then. Average points-per-game therefore rose
/// with age from population composition alone, the polynomial was fitted to
/// that rising series, and the result was a curve on which prime years
/// remaining <em>increased</em> with age — a 23-year-old Malik Nabers had zero
/// and a 27-year-old CeeDee Lamb had four.
/// </para>
///
/// <para>
/// This version uses the delta method: for each player with consecutive
/// seasons, take the year-over-year change in per-game production, average
/// those changes within each age transition, and chain them into a curve. The
/// player is held constant across the comparison, so who is in the league
/// cannot masquerade as aging.
/// </para>
/// </summary>
/// <param name="estimatorTrusted">
/// Gate on returning fitted curves at all. False while the chained-ratio
/// estimator is known mis-specified (FAN-157, 2026-09-07) — it cannot separate a
/// constant sample-selection drift from aging, and that drift drags every fitted
/// peak early. While false, all diagnostics still run and log; only the fitted
/// result is discarded, and each position falls back to the analytic curve.
///
/// <para>
/// A constructor argument rather than a buried constant so the choice is stated
/// once, out loud, at the composition root, and so tests can exercise the
/// estimator without the gate. See the gate itself for the conditions on
/// flipping it.
/// </para>
/// </param>
public class AgingCurveService(
    IPlayerGameLogRepository gameLogRepository,
    IPlayerRepository playerRepository,
    IAgingCurveRepository agingCurveRepository,
    ILogger<AgingCurveService> logger,
    bool estimatorTrusted = false) : IAgingCurveService
{
    /// <summary>
    /// Minimum games a player must play in a season for that season to be one
    /// end of a transition. A three-game season is mostly injury, and treating
    /// it as a data point about age turns availability into decline.
    /// </summary>
    private const int MinGamesPerSeason = 6;

    /// <summary>
    /// Minimum player-transitions needed before an age→age+1 step is trusted.
    /// Below this the geometric mean is one or two careers, not a population.
    /// </summary>
    private const int MinTransitionsPerAge = 10;

    /// <summary>
    /// Minimum length of the contiguous run of trusted transitions. Fewer than
    /// this and there is not enough of a curve to fit a cubic to honestly.
    /// </summary>
    private const int MinContiguousTransitions = 4;

    private const int MinLogsForFit = 50;

    /// <summary>
    /// Added to both sides of the year-over-year ratio to bound the blow-up when
    /// the earlier season is near zero: log((to + c) / (from + c)).
    ///
    /// <para>
    /// This replaces a minimum-baseline filter that excluded a transition when
    /// the <em>earlier</em> season fell below a floor while keeping it when the
    /// <em>later</em> one did. That asymmetry dropped recoveries and kept
    /// collapses, pushing every age bucket's mean below zero and producing a
    /// monotonically declining curve at all four positions. Damping bounds the
    /// same noise without deleting anyone, and it does so symmetrically.
    /// </para>
    ///
    /// <para>
    /// Roughly a replacement-level per-game score, so a genuine starter's
    /// year-over-year change is barely damped while a fringe player's 0.4 → 8.0
    /// is pulled in from a 20× ratio to about 3×.
    /// </para>
    /// </summary>
    private const double RatioDampening = 3.0;

    /// <summary>
    /// Distinct season-to-season pairs required before a fitted curve is
    /// trusted. Player count is not the relevant sample size here: any number of
    /// players measured across a single pair of seasons carries about one degree
    /// of freedom about aging, because a league-wide change between those two
    /// years lands in every age bucket at once and is indistinguishable from an
    /// age effect.
    /// </summary>
    private const int MinDistinctSeasonPairs = 2;

    /// <summary>
    /// How far a fitted peak age may sit from the position's prior before the
    /// curve is refused. A tripwire against shipping a mis-specified curve into
    /// a scheduled job, not a belief about football. See the block where it is
    /// applied for why it exists and when to remove it.
    /// </summary>
    private const int MaxPeakDeviationFromPrior = 2;


    public async Task<List<AgingCurveDocument>> BuildAllCurvesAsync(CancellationToken ct = default)
    {
        var ageMap = await BuildAgeMapAsync(ct);
        logger.LogInformation("Age map built — {Count} players with known age", ageMap.Count);

        var curves = new List<AgingCurveDocument>();

        foreach (var position in AgingFallbackCurve.ModelledPositions)
        {
            var curve = await BuildCurveForPositionAsync(position, ageMap, ct);
            curves.Add(curve);
        }

        return curves;
    }

    /// <summary>
    /// FAN-157 — this used to load the age window and then return
    /// <c>GetDefaultMultiplier</c> unconditionally, never consulting the stored
    /// curve at all. So every caller of this method got the hardcoded fallback
    /// while <c>CareerSimulationService</c> used the real curve: two aging
    /// models in one codebase, silently disagreeing. It now reads the stored
    /// curve and falls back only when there isn't one.
    /// </summary>
    public async Task<double> GetAgeMultiplierAsync(
        string position, int age, CancellationToken ct = default)
    {
        var curve = await agingCurveRepository.GetByPositionAsync(position, ct);

        if (curve is not null && !curve.IsDefaultCurve && curve.AgeValueMap.TryGetValue(age, out var stored))
            return stored / 100.0;

        return AgingFallbackCurve.Multiplier(position, age);
    }

    public double EvaluateAtAge(AgingCurveDocument curve, int age)
    {
        if (curve.AgeValueMap.TryGetValue(age, out var val))
            return val;

        if (curve.Coefficients.Length == 4)
        {
            double result = curve.Coefficients[0]
                          + curve.Coefficients[1] * age
                          + curve.Coefficients[2] * Math.Pow(age, 2)
                          + curve.Coefficients[3] * Math.Pow(age, 3);
            return Math.Max(0, Math.Min(100, result));
        }

        return AgingFallbackCurve.Multiplier(curve.Position, age) * 100.0;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task<Dictionary<string, int>> BuildAgeMapAsync(CancellationToken ct)
    {
        var ageMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var posEnum in new[] { Position.QB, Position.RB, Position.WR, Position.TE })
        {
            var players = await playerRepository.GetByPositionAsync(posEnum, ct);
            foreach (var p in players)
            {
                if (p.SleeperPlayerId is not null && p.Age.HasValue)
                    ageMap[p.SleeperPlayerId] = p.Age.Value;
            }
        }

        return ageMap;
    }

    private async Task<AgingCurveDocument> BuildCurveForPositionAsync(
        string position,
        Dictionary<string, int> ageMap,
        CancellationToken ct)
    {
        var window = AgingFallbackCurve.WindowFor(position);
        var logs = await gameLogRepository.GetByPositionAsync(position, ct);

        if (logs.Count < MinLogsForFit)
        {
            logger.LogWarning(
                "{Position}: only {Count} logs — using fallback curve", position, logs.Count);
            return BuildDefaultCurve(position);
        }

        // ── Step 1: collapse logs to player-seasons ──────────────────────
        // Keyed by (player, season) so a season is one observation regardless of
        // how many games it contains, and a 17-game year cannot outvote a 9-game
        // year in the ratio that follows.
        var playerSeasons = new Dictionary<(string Player, int Season), (double Points, int Games)>();

        foreach (var log in logs)
        {
            if (log.SleeperPlayerId is null) continue;
            if (!ageMap.ContainsKey(log.SleeperPlayerId)) continue;

            var points = ComputeFantasyPointsPpr(log);

            // Zero-point games are kept here, unlike the old implementation,
            // which dropped them with `if (fppg <= 0) continue`. A season's
            // per-game average has to include the games a player was on the
            // field and did nothing, or a declining player's bad weeks are
            // deleted and his decline with them. Games he did not play are
            // simply absent from the logs and are correctly not counted.
            var key = (log.SleeperPlayerId, log.Season);
            var existing = playerSeasons.GetValueOrDefault(key);
            playerSeasons[key] = (existing.Points + points, existing.Games + 1);
        }

        // ── Step 2: year-over-year transitions, held within one player ────
        // The whole point of the method. `logRatios[a]` accumulates
        // log(fppg at age a+1 / fppg at age a) across every player who played
        // both seasons. Logs rather than raw ratios so the average is a
        // geometric mean: a doubling and a halving cancel, which is what you
        // want from a multiplicative quantity, and one 6x outlier cannot drag a
        // bucket the way it would in an arithmetic mean.
        var referenceYear = ReferenceYearFor(logs);
        var logRatios = new Dictionary<int, List<(double LogRatio, double Weight)>>();
        var seasonPairsUsed = new HashSet<int>();

        var byPlayer = playerSeasons
            .GroupBy(kv => kv.Key.Player)
            .ToDictionary(g => g.Key, g => g.ToDictionary(kv => kv.Key.Season, kv => kv.Value));

        foreach (var (sleeperId, seasons) in byPlayer)
        {
            var currentAge = ageMap[sleeperId];

            foreach (var (season, cur) in seasons)
            {
                if (!seasons.TryGetValue(season + 1, out var next)) continue;
                if (cur.Games < MinGamesPerSeason || next.Games < MinGamesPerSeason) continue;

                var fromFppg = cur.Points / cur.Games;
                var toFppg = next.Points / next.Games;

                // ── The 2026-09-07 defect, fixed ──────────────────────────
                //
                // This used to read: if (fromFppg < 2.0 || toFppg <= 0) continue;
                //
                // That filter is asymmetric, and asymmetric in one direction. A
                // player who went 1.5 → 9.0 was excluded; a player who went
                // 9.0 → 1.5 was kept. Every age bucket therefore saw the
                // declines and not the matching recoveries, so the average
                // log-ratio was pushed below zero everywhere — and a uniformly
                // negative growth series chains into a monotonically decreasing
                // index, which peaks at whatever age the run starts from.
                //
                // That is exactly what the first live run produced: WR peaked at
                // 22 against a run starting at 21, TE at 24 from a run starting
                // at 22, and QB and RB were rejected for peaking at their window
                // floor. Four positions, one signature, and it was mine — not the
                // data's.
                //
                // The fix is a damped ratio instead of a floor: adding a constant
                // to both sides bounds the blow-up near zero without deleting
                // anybody. A 0.4 → 8.0 season change becomes a 1.2 log-ratio
                // rather than a 3.0, and a 9.0 → 1.5 collapse is still counted at
                // full weight. Nothing is excluded on the strength of the season
                // it started from.
                var logRatio = Math.Log((toFppg + RatioDampening) / (fromFppg + RatioDampening));

                // Weight by the harmonic mean of games played across the pair,
                // the standard weighting for this estimator. A 6-and-7-game
                // transition is a far noisier observation than a 17-and-17, and
                // counting them equally is how a handful of part-seasons ends up
                // steering an age bucket.
                var weight = 2.0 * cur.Games * next.Games / (cur.Games + next.Games);

                // Age at the earlier season of the pair. `Player.Age` is age
                // today, so this is accurate to about a year depending on where
                // the birthday falls — which the delta method tolerates in a way
                // the old cross-section did not: a constant per-player offset
                // moves a player's transitions between adjacent buckets but
                // leaves each transition exactly one year long, so the error
                // smears the curve slightly rather than biasing its direction.
                // Exact ages need Player.BirthDate; tracked separately.
                var fromAge = currentAge - (referenceYear - season);
                if (fromAge < window.MinAge || fromAge >= window.MaxAge) continue;

                if (!logRatios.TryGetValue(fromAge, out var list))
                {
                    list = [];
                    logRatios[fromAge] = list;
                }
                list.Add((logRatio, weight));
                seasonPairsUsed.Add(season);
            }
        }

        // ── Step 3: weighted geometric-mean growth per age transition ────
        //
        // The sample size that matters for a delta estimator is not the number
        // of player transitions — it is the number of distinct season-to-season
        // pairs those transitions are drawn from. Three hundred players all
        // measured across the same single pair of seasons look like a large
        // sample and carry roughly one degree of freedom about aging: any
        // league-wide shift between those two years is indistinguishable from an
        // age effect, in every bucket at once.
        if (seasonPairsUsed.Count < MinDistinctSeasonPairs)
        {
            logger.LogWarning(
                "{Position}: transitions come from only {Pairs} distinct season pair(s) " +
                "({Seasons}) — too few to separate aging from a single year's league-wide " +
                "shift. Using fallback curve.",
                position, seasonPairsUsed.Count,
                string.Join(", ", seasonPairsUsed.OrderBy(s => s).Select(s => $"{s}→{s + 1}")));
            return BuildDefaultCurve(position);
        }

        var growth = logRatios
            .Where(kv => kv.Value.Count >= MinTransitionsPerAge)
            .ToDictionary(
                kv => kv.Key,
                kv => Math.Exp(
                    kv.Value.Sum(v => v.LogRatio * v.Weight) / kv.Value.Sum(v => v.Weight)));

        if (growth.Count < MinContiguousTransitions)
        {
            logger.LogWarning(
                "{Position}: only {Count} age transitions cleared the {Min}-player minimum — using fallback curve",
                position, growth.Count, MinTransitionsPerAge);
            return BuildDefaultCurve(position);
        }

        // Emit the growth factors themselves. The first live run had to be
        // diagnosed backwards from four rejected/implausible peak ages; the
        // per-age factors say directly whether the series is negative
        // everywhere (a filtering artefact) or genuinely humped.
        logger.LogInformation(
            "{Position} year-over-year growth by age: {Growth} (from {Pairs} season pairs)",
            position,
            string.Join(" ", growth.OrderBy(kv => kv.Key)
                .Select(kv => $"{kv.Key}→{kv.Key + 1}:{kv.Value:F3}")),
            seasonPairsUsed.Count);

        // ── Step 4: chain the growth factors into an index ───────────────
        // Only over the longest unbroken run of transitions. A gap cannot be
        // chained across without inventing the missing step, and filling it with
        // 1.0 would silently flatten the curve exactly where the data is
        // thinnest.
        var (runStart, runEnd) = LongestContiguousRun(growth.Keys);
        if (runEnd - runStart + 1 < MinContiguousTransitions)
        {
            logger.LogWarning(
                "{Position}: longest unbroken transition run is {Len} ages — using fallback curve",
                position, runEnd - runStart + 1);
            return BuildDefaultCurve(position);
        }

        // ── The drift check ──────────────────────────────────────────────
        //
        // Measured 2026-09-07 on the first clean run: WR incumbents came out at
        // -6.9% per year averaged across every age from 21 to 31, RB -6.4%, TE
        // -3.7%. A receiver cannot lose 7% of his production every year from 21
        // to 31 and still be a starter at 31; that is not aging, it is the
        // sample selecting itself.
        //
        // Only players with six-plus games in BOTH seasons enter a transition,
        // which is a positively-selected group in the earlier season, and such a
        // group regresses toward the mean of the full population — including the
        // players it excluded. The result is a roughly constant multiplicative
        // drift sitting underneath the real age signal.
        //
        // It matters because the peak is exactly where growth crosses 1.0, so a
        // constant negative drift drags the crossing — and the peak — earlier.
        // It is also NOT safely removable by dividing it out: that assumes the
        // average age effect across the observed range is zero, which is false
        // whenever the range is asymmetric about the peak. (Confirmed against
        // this file's own test fixture, whose true curve has a genuine net
        // decline over 22-33 that de-drifting would erase.)
        //
        // Separating the two needs an estimator that can hold season and player
        // constant simultaneously — player and season fixed effects with a
        // polynomial age term — not a chained ratio. Until that exists, this
        // logs the drift so it is visible in every run rather than inferred from
        // a wrong peak age three builds later.
        var driftLog = logRatios
            .Where(kv => kv.Key >= runStart && kv.Key <= runEnd)
            .SelectMany(kv => kv.Value)
            .ToList();
        var meanDrift = driftLog.Count > 0
            ? Math.Exp(driftLog.Sum(v => v.LogRatio * v.Weight) / driftLog.Sum(v => v.Weight))
            : 1.0;

        logger.LogInformation(
            "{Position} sample-wide mean year-over-year growth: {Drift:F4} ({Pct:+0.0;-0.0}% per year). " +
            "A value far from 1.0 across all ages is selection drift, not aging — it drags the " +
            "fitted peak earlier.",
            position, meanDrift, (meanDrift - 1.0) * 100.0);

        var index = new Dictionary<int, double> { [runStart] = 1.0 };
        for (var age = runStart; age <= runEnd; age++)
            index[age + 1] = index[age] * growth[age];

        // ── Step 5: fit, evaluate, validate ──────────────────────────────
        var maxIndex = index.Values.Max();
        if (maxIndex <= 0) return BuildDefaultCurve(position);

        var ages = index.Keys.OrderBy(a => a).Select(a => (double)a).ToArray();
        var normalized = ages.Select(a => index[(int)a] / maxIndex * 100.0).ToArray();

        double[] coefficients;
        try
        {
            coefficients = Fit.Polynomial(ages, normalized, 3);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Polynomial fit failed for {Position} — using fallback curve", position);
            return BuildDefaultCurve(position);
        }

        // Evaluate the cubic only where there is data behind it, and hold the
        // endpoint value outside that span.
        //
        // A degree-3 polynomial extrapolated past its own data can turn upward —
        // WR transitions run out around 33 while the window reaches 35, so two
        // extrapolated years could produce a curve that declines through a
        // player's thirties and then flares back up at the end. That is exactly
        // the "rises with age" shape this ticket exists to eliminate, arriving by
        // a different route, and it would either trip the validation below and
        // silently cost us the whole curve, or pass and be wrong.
        //
        // Holding the endpoint is the honest answer: past the last observed
        // transition we have nothing to say, so the curve stops changing rather
        // than inventing a trend.
        var spanStart = runStart;
        var spanEnd = runEnd + 1;

        double Evaluate(int age) =>
            coefficients[0]
          + coefficients[1] * age
          + coefficients[2] * Math.Pow(age, 2)
          + coefficients[3] * Math.Pow(age, 3);

        var ageValueMap = new Dictionary<int, double>();
        for (int age = window.MinAge; age <= window.MaxAge; age++)
        {
            var evalAge = Math.Clamp(age, spanStart, spanEnd);
            ageValueMap[age] = Math.Max(0, Math.Min(100, Evaluate(evalAge)));
        }

        // Renormalise after clamping so the curve's own peak is 100 — the clamp
        // above can shave the maximum, and a curve whose peak is 94 would apply
        // a 6% penalty to a player at his own best age.
        var evaluatedMax = ageValueMap.Values.Max();
        if (evaluatedMax > 0 && Math.Abs(evaluatedMax - 100.0) > 0.01)
        {
            foreach (var age in ageValueMap.Keys.ToList())
                ageValueMap[age] = ageValueMap[age] / evaluatedMax * 100.0;
        }

        // The gate FAN-157 asks for. A curve that rises with age is the failure
        // this ticket is about, and it was stored and served for a day with
        // nothing objecting. Refusing to store it is the difference between a
        // bug that shows up in the next calibration run and one that shows up
        // when Paul notices a 23-year-old with no prime years left.
        if (!AgingFallbackCurve.IsPlausibleAgingCurve(
                ageValueMap, window.MinAge, window.MaxAge, out var failureReason))
        {
            logger.LogError(
                "{Position}: fitted aging curve rejected — {Reason}. Falling back. " +
                "Transitions used: {Transitions} ages spanning {RunStart}-{RunEnd}.",
                position, failureReason, growth.Count, runStart, runEnd + 1);
            return BuildDefaultCurve(position);
        }

        var peakAge = ageValueMap.MaxBy(kv => kv.Value).Key;

        // ── Tripwire, not a model ────────────────────────────────────────
        //
        // Deliberately opinionated, and deliberately temporary. The estimator is
        // known mis-specified as of 2026-09-07 (see the drift note above), and
        // the curves it produced — WR peaking at 23 against a prior of 26, TE at
        // 24 against 27 — are live the moment they are written, because
        // RecalculateDynastyValuationsJob reads this collection on a schedule
        // and nobody has to press anything.
        //
        // So a fitted peak more than two years from the position's prior is
        // refused. This WILL block a correct curve that legitimately disagrees
        // with the prior, which is the cost of not shipping a wrong one
        // unattended. When the fixed-effects estimator lands and is trusted,
        // widen this or delete it — do not quietly work around it.
        if (Math.Abs(peakAge - window.PeakAge) > MaxPeakDeviationFromPrior)
        {
            logger.LogError(
                "{Position}: fitted peak age {Fitted} is more than {Max} years from the prior " +
                "peak {Prior} — refusing to store. Mean sample drift was {Drift:F4}; a fitted " +
                "peak pulled early is the signature of selection drift the chained-ratio " +
                "estimator cannot remove. Falling back.",
                position, peakAge, MaxPeakDeviationFromPrior, window.PeakAge, meanDrift);
            return BuildDefaultCurve(position);
        }

        var transitionsUsed = logRatios
            .Where(kv => kv.Key >= runStart && kv.Key <= runEnd)
            .Sum(kv => kv.Value.Count);

        logger.LogInformation(
            "{Position} curve built (longitudinal) — peak age {PeakAge}, " +
            "{AgeSteps} age steps over run {RunStart}-{RunEnd} from {Transitions} player transitions",
            position, peakAge, growth.Count, runStart, runEnd + 1, transitionsUsed);

        // ── The estimator is not trusted yet. Nothing it fits gets stored. ──
        //
        // Everything above still runs, deliberately: the growth-by-age factors,
        // the sample-wide drift, the single-peak check and the prior-band
        // tripwire are the instruments for fixing this, and they only work if
        // they keep producing readings. What stops here is the *storing*.
        //
        // Why a switch rather than a tighter threshold. On 2026-09-07 the
        // prior-band tripwire was set at ±2 and three of four positions fell
        // back — but RB fitted a peak of 23 against a prior of 24, passed on a
        // one-year margin, and went live on its own. That is the worst of the
        // three outcomes: RB valuations computed by a known mis-specified
        // estimator while QB, WR and TE used the analytic fallback, with RB's
        // tail flat at 61 through age 32 where the fallback reads 10. A
        // threshold that admits one position by luck is not a safety property.
        //
        // The measured defect: chained year-over-year ratios carry a roughly
        // constant selection drift (WR -6.9%/yr, RB -6.4%, TE -3.7% across every
        // age) that a chained estimator cannot separate from aging, because a
        // drift constant in age is indistinguishable from a linear age trend.
        // It drags the fitted peak early — which is why all three fitted curves
        // peaked within a year of each other regardless of position.
        //
        // Flip this to true (in DependencyInjection) when the player-and-season
        // fixed-effects estimator replaces the chained ratio AND a build
        // produces peaks near the priors without the tripwire doing the work.
        // Flipping it for any other reason ships the same wrong curves into
        // RecalculateDynastyValuationsJob, which reads this collection on a
        // schedule with nobody watching.
        if (!estimatorTrusted)
        {
            logger.LogWarning(
                "{Position}: fitted curve discarded — the longitudinal estimator is gated off " +
                "(EstimatorTrusted = false) pending the fixed-effects rewrite. Would have stored " +
                "peak age {PeakAge} against prior {Prior}, mean drift {Drift:F4}. Using fallback.",
                position, peakAge, window.PeakAge, meanDrift);
            return BuildDefaultCurve(position);
        }

        return new AgingCurveDocument
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Position = position,
            Coefficients = coefficients,
            PeakAge = peakAge,
            PeakValue = ageValueMap.Values.Max(),
            MinAge = window.MinAge,
            MaxAge = window.MaxAge,
            AgeValueMap = ageValueMap,
            ComputedAt = DateTime.UtcNow,
            // Transitions, not game logs. The old code stored logs.Count, which
            // overstated the sample by an order of magnitude and did not match
            // the field's own documented meaning. For a delta estimator the
            // honest sample size is the number of year-over-year pairs the fit
            // actually consumed.
            SampleSize = transitionsUsed,
            IsDefaultCurve = false
        };
    }

    /// <summary>
    /// The year <c>Player.Age</c> is measured against. Previously a hardcoded
    /// <c>2026</c>, which is simply wrong from 2027 onward and silently shifts
    /// every age-at-season by one when it turns over.
    /// </summary>
    private static int ReferenceYearFor(IReadOnlyCollection<PlayerGameLogDocument> logs)
    {
        // Ages come from the SQL Player table and are ages *now*, so the
        // reference is the current calendar year, not the latest season in the
        // data — using the data's own max season would make every age wrong by
        // however far behind the logs happen to be.
        var now = DateTime.UtcNow.Year;

        // Guard against a clock or dataset far enough out of step that ages
        // would go negative; fall back to the newest season present.
        var latestSeason = logs.Count > 0 ? logs.Max(l => l.Season) : now;
        return now < latestSeason ? latestSeason : now;
    }

    private static (int Start, int End) LongestContiguousRun(IEnumerable<int> keys)
    {
        var sorted = keys.OrderBy(k => k).ToList();
        if (sorted.Count == 0) return (0, -1);

        int bestStart = sorted[0], bestEnd = sorted[0];
        int curStart = sorted[0], curEnd = sorted[0];

        for (var i = 1; i < sorted.Count; i++)
        {
            if (sorted[i] == curEnd + 1)
            {
                curEnd = sorted[i];
            }
            else
            {
                curStart = sorted[i];
                curEnd = sorted[i];
            }

            if (curEnd - curStart > bestEnd - bestStart)
            {
                bestStart = curStart;
                bestEnd = curEnd;
            }
        }

        return (bestStart, bestEnd);
    }

    private static double ComputeFantasyPointsPpr(PlayerGameLogDocument log)
    {
        if (log.FantasyPointsPpr.HasValue && log.FantasyPointsPpr.Value > 0)
            return (double)log.FantasyPointsPpr.Value;

        return (double)(
            log.PassingYards / 25m
          + log.PassingTds * 4m
          - log.Interceptions * 2m
          + log.RushingYards / 10m
          + log.RushingTds * 6m
          + log.Receptions * 1m
          + log.ReceivingYards / 10m
          + log.ReceivingTds * 6m);
    }

    private static AgingCurveDocument BuildDefaultCurve(string position)
    {
        var window = AgingFallbackCurve.WindowFor(position);

        var ageValueMap = new Dictionary<int, double>();
        for (int age = window.MinAge; age <= window.MaxAge; age++)
            ageValueMap[age] = AgingFallbackCurve.Multiplier(position, age) * 100.0;

        return new AgingCurveDocument
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Position = position,
            Coefficients = [],
            PeakAge = window.PeakAge,
            PeakValue = 100.0,
            MinAge = window.MinAge,
            MaxAge = window.MaxAge,
            AgeValueMap = ageValueMap,
            ComputedAt = DateTime.UtcNow,
            SampleSize = 0,
            IsDefaultCurve = true
        };
    }
}

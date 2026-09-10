using FF.Domain.Documents;
using FF.Domain.Enums;
using FF.Domain.ValueObjects;

namespace FF.Application.Services;

/// <summary>
/// One position's replacement level for one projected season, resolved against a
/// league shape. <paramref name="Cutoff"/> is how many players at this position the
/// league starts in aggregate — the index the level was read from.
/// </summary>
public sealed record PositionalReplacementLevel(
    int Year,
    string Position,
    double Level,
    int Cutoff,
    bool PoolExhausted);

/// <summary>
/// A career's discounted value over replacement, with the working kept.
///
/// <para>
/// <paramref name="YearsWithoutCurve"/> counts projected seasons that had no curve
/// to price against and were therefore skipped. It is carried rather than folded
/// into the total because a player silently missing two of his five seasons scores
/// lower for a reason that has nothing to do with the player, and a run where that
/// count is non-zero is a run whose curves are stale relative to its simulations.
/// </para>
/// </summary>
public sealed record CareerValueOverReplacement(
    double Total,
    int YearsPriced,
    int YearsWithoutCurve);

/// <summary>
/// Value over replacement, per projected season — FAN-153 phase 2.
///
/// <para><b>What this replaces.</b> Until now the pipeline compared players across
/// positions by their raw discounted point totals, corrected by two hand-written
/// stand-ins pulling in opposite directions: a per-position scarcity multiplier
/// applied before normalization, and a rank-keyed cap table applied after the blend.
/// FAN-166 measured the cap tables at ρ 0.2172, all of it cross-position, and
/// FAN-168 then showed the whole-board number <i>falling</i> while every
/// within-position ρ rose — components improving while the assembly got worse.
/// Comparing point totals across positions is the defect: measured 2026-09-08, a
/// replacement quarterback scores 227.3 in a standard league against a replacement
/// receiver's 106.4, so a QB at 300 clears his bar by 72.7 while a WR at 200 clears
/// his by 93.6. Subtracting the baseline is what makes those two numbers
/// comparable, and it does it by construction rather than by a table.</para>
///
/// <para><b>Per season, not per career.</b> Dynasty value is a discounted sum of
/// future seasons, so the honest form of the correction is a discounted sum of each
/// season's surplus over <i>that season's</i> replacement level. A single
/// subtraction against a career-total baseline would conflate "good" with "young":
/// a 22-year-old backup accumulates career value through longevity alone and would
/// be credited twice for the same fact.</para>
///
/// <para><b>Negative surplus is kept.</b> A player projected below replacement
/// scores negative and stays on the board at that rank. Clamping at zero would put
/// hundreds of players on an identical value, and a tie is not a ranking — it is
/// FAN-170's defect (a low projection treated as an absence) at scale. The known
/// artifact of summing unclamped is that a deficit accumulates with career length,
/// so deep in the tail an old backup can outrank a young one; that is watched in
/// the worst-20 rather than pre-emptively patched, because every patch this
/// pipeline has carried began as a fix for something nobody had measured.</para>
/// </summary>
public static class ValueOverReplacementCalculator
{
    /// <summary>
    /// The league size the board is priced for.
    ///
    /// <para>
    /// The dynasty board is global — one set of valuations, not one per league — so
    /// it has to commit to a shape. Twelve teams is both the modal league size and
    /// the shape implied by the FantasyPros Superflex dynasty consensus the
    /// calibration harness scores against, which makes the comparison honest rather
    /// than a comparison between two different games (the third acceptance item on
    /// FAN-153).
    /// </para>
    ///
    /// <para>
    /// A league that is not this shape is not stuck with this answer: the curves are
    /// stored rather than the level precisely so any roster configuration can
    /// resolve its own cutoff at read time. This constant is the board's default,
    /// not the model's assumption.
    /// </para>
    /// </summary>
    public const int CanonicalTeamCount = 12;

    /// <summary>
    /// The roster shape a scoring format implies. Superflex formats add a
    /// SUPER_FLEX slot, which is the whole mechanism: quarterbacks become eligible
    /// for a flex, outbid everyone else for it, and the QB cutoff moves a full round
    /// deeper — measured 2026-09-08 as a 22.5% drop in the QB baseline from
    /// identical projections.
    /// </summary>
    public static RosterConfiguration LeagueShapeFor(ScoringFormat scoringFormat)
        => scoringFormat is ScoringFormat.Superflex or ScoringFormat.SuperflexFullPpr
            ? RosterConfiguration.Superflex
            : RosterConfiguration.Standard;

    /// <summary>
    /// The key a positional value curve is stored and read under.
    ///
    /// <para>
    /// <see cref="ScoringFormat"/> carries two independent things in one enum: how
    /// points are scored, and whether the league plays a superflex slot.
    /// <c>Superflex</c> <i>is</i> Half-PPR scoring, and <c>SuperflexFullPpr</c> is
    /// Full-PPR scoring — the enum's own comments say so. A curve is a distribution
    /// of projected season points, so only the first half of that pairing can
    /// change it; the roster shape is applied afterwards, at read time, by indexing
    /// into the same curve at a different cutoff.
    /// </para>
    ///
    /// <para>
    /// Keying storage on the whole enum would therefore file two identical curves
    /// under two names and make a superflex read miss a Half-PPR build — which is
    /// exactly what it did on first run: curves built under <c>Superflex</c>, a DFV
    /// run defaulting to <c>HalfPpr</c>, and nothing found. Collapsing the key onto
    /// the scoring half makes the two league shapes read literally the same stored
    /// documents, which is also what makes the ticket's acceptance criterion a
    /// statement about the league rather than about the data.
    /// </para>
    /// </summary>
    public static string CurveScoringKey(ScoringFormat scoringFormat) => scoringFormat switch
    {
        ScoringFormat.Standard => nameof(ScoringFormat.Standard),
        ScoringFormat.FullPpr or ScoringFormat.SuperflexFullPpr => nameof(ScoringFormat.FullPpr),
        _ => nameof(ScoringFormat.HalfPpr)
    };

    /// <summary>
    /// Resolves a replacement level for every (projected year, position) present in
    /// the stored curves, for one league shape.
    ///
    /// <para>
    /// The allocation itself is <see cref="ReplacementLevelService"/>'s — one
    /// implementation of the flex rules, shared with the live L3 path, so the board
    /// and a league's own read cannot drift apart.
    /// </para>
    ///
    /// <para>
    /// A position with no curve for a year is left out of the result rather than
    /// given a level of zero. Zero is not a small replacement level, it is no
    /// replacement level at all, and it would credit every player at that position
    /// with his entire point total as surplus.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// A cutoff ran past the stored depth while the pool goes deeper still. The
    /// stored curve is too shallow for this league shape and the fix is a deeper
    /// curve, not a guess — answering with the weakest stored value would read as an
    /// exhausted pool and inflate every value above it.
    /// </exception>
    public static IReadOnlyDictionary<(int Year, string Position), PositionalReplacementLevel> ResolveLevels(
        IReadOnlyList<PositionalValueCurveDocument> curves,
        RosterConfiguration config,
        int teamCount)
    {
        ArgumentNullException.ThrowIfNull(curves);
        ArgumentNullException.ThrowIfNull(config);

        var levels = new Dictionary<(int Year, string Position), PositionalReplacementLevel>();

        foreach (var yearGroup in curves.GroupBy(c => c.Year).OrderBy(g => g.Key))
        {
            // Positions this year actually has a curve for. Everything below is
            // keyed off this set, so a missing position stays missing instead of
            // resolving to an empty curve and a fabricated zero.
            var present = yearGroup
                .GroupBy(c => Normalize(c.Position))
                .Where(g => g.Key is not null)
                .ToDictionary(g => g.Key!, g => g.First());

            var descending = present.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<decimal>)kv.Value.DescendingSeasonValues
                    .Select(v => (decimal)v)
                    .ToList(),
                StringComparer.OrdinalIgnoreCase);

            var poolSizes = present.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.PoolSize,
                StringComparer.OrdinalIgnoreCase);

            var resolved = ReplacementLevelService.ResolveFromCurves(
                descending, poolSizes, config, teamCount);

            foreach (var (position, replacement) in resolved)
            {
                if (!present.ContainsKey(position)) continue;

                if (replacement.BeyondStoredDepth)
                    throw new InvalidOperationException(
                        $"Positional value curve for {yearGroup.Key} {position} is stored to depth "
                        + $"{descending[position].Count} but this league shape starts "
                        + $"{replacement.StartersAbsorbed} of them. Rebuild the curves at a greater "
                        + "depth (POST /api/v1/admin/jobs/build-value-curves with a larger Depth) — "
                        + "clamping to the weakest stored value would read as an exhausted pool and "
                        + "inflate every value above it.");

                if (replacement.StructuralLevel is null) continue;

                levels[(yearGroup.Key, position)] = new PositionalReplacementLevel(
                    Year:          yearGroup.Key,
                    Position:      position,
                    Level:         (double)replacement.StructuralLevel.Value,
                    Cutoff:        replacement.StartersAbsorbed,
                    PoolExhausted: replacement.PoolExhausted);
            }
        }

        return levels;
    }

    /// <summary>
    /// One career's discounted value over replacement.
    ///
    /// <para>
    /// The discount rate stays per-position and stays where it was. It is a second
    /// cross-position knob and arguably now redundant — the aging curve already
    /// shortens running back careers, so discounting them harder may be counting the
    /// same fact twice — but changing two things at once produces one number that
    /// explains neither. It comes out, or does not, against its own measurement.
    /// </para>
    ///
    /// <para>
    /// <paramref name="projectionMultiplier"/> discounts the projection itself,
    /// <b>before</b> the replacement level is subtracted. That placement is not
    /// cosmetic and was got wrong once already, so it is worth stating why.
    /// </para>
    ///
    /// <para>
    /// A multiplier of this kind expresses "this player's projected production is
    /// too high" — the free-agent penalty is the live example. Applying it to the
    /// surplus instead multiplies a difference rather than a projection, and the
    /// two are not the same correction: for a player with gross projection
    /// <c>G</c> against a non-discounted rival at <c>G'</c> and a replacement
    /// constant <c>K</c>, penalising the projection puts the crossover at
    /// <c>G' &lt; mG</c>, while penalising the surplus moves it to
    /// <c>G' &lt; mG + (1−m)K</c> — every penalised player gains <c>(1−m)K</c> for
    /// free. Measured 2026-09-08 on the first phase 2 run: quarterbacks, the one
    /// position where the free-agent penalty cannot fire, came back bit-identical
    /// at ρ 0.6799 while RB, WR and TE all regressed, RB hardest at −0.0392 with a
    /// worst-20 full of free-agent rookies. K for RB is 238.7, so the penalty was
    /// handing them 95 points of gross production.
    /// </para>
    /// </summary>
    public static CareerValueOverReplacement Compute(
        CareerSimulationDocument careerSim,
        string position,
        double discountRate,
        IReadOnlyDictionary<(int Year, string Position), PositionalReplacementLevel> levels,
        double projectionMultiplier = 1.0)
    {
        ArgumentNullException.ThrowIfNull(careerSim);
        ArgumentNullException.ThrowIfNull(levels);

        var normalized = Normalize(position);
        if (normalized is null || careerSim.YearProjections.Count == 0)
            return new CareerValueOverReplacement(0, 0, 0);

        double total = 0;
        var priced = 0;
        var withoutCurve = 0;

        foreach (var year in careerSim.YearProjections)
        {
            if (!levels.TryGetValue((year.Year, normalized), out var level))
            {
                withoutCurve++;
                continue;
            }

            var yearIndex = year.Year - careerSim.Season;
            var projected = year.SeasonValue * projectionMultiplier;
            total += (projected - level.Level) / Math.Pow(1 + discountRate, yearIndex);
            priced++;
        }

        return new CareerValueOverReplacement(total, priced, withoutCurve);
    }

    private static readonly string[] ScoredPositions = ["QB", "RB", "WR", "TE"];

    private static string? Normalize(string position)
        => ScoredPositions.FirstOrDefault(
            p => string.Equals(p, position, StringComparison.OrdinalIgnoreCase));
}

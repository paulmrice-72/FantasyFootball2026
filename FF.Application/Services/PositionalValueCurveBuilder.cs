using FF.Domain.Documents;

namespace FF.Application.Services;

/// <summary>
/// One position's projected value distribution for one future season, descending.
/// <paramref name="PoolSize"/> is the count before truncation to the requested depth.
/// </summary>
public sealed record PositionalValueCurve(
    int Year,
    string Position,
    IReadOnlyList<double> DescendingSeasonValues,
    int PoolSize);

/// <summary>
/// Builds positional value curves from career simulations — FAN-153.
///
/// <para>
/// Pure and static, in the same spirit as <see cref="ReplacementLevelService"/>:
/// everything arrives as arguments, so the curves are reproducible and testable
/// without a database.
/// </para>
///
/// <para><b>Why this exists.</b> The dynasty pipeline has never had a
/// cross-position basis. It has had two crude stand-ins pulling against each other
/// — a per-position scarcity multiplier applied to raw DFV, and a hand-written
/// rank-keyed cap table applied after the blend. FAN-166 measured the cap tables at
/// rho 0.2172, all of it cross-position, and FAN-168 then showed that improving
/// within-position ordering <i>lowers</i> the whole-board number while no ladder
/// exists. Value over replacement is the ladder; this is its input.
/// </para>
///
/// <para><b>Why per-year rather than per-career.</b> Dynasty value is a discounted
/// sum of future seasons, so the honest form of value over replacement is a
/// discounted sum of each season's surplus over that season's replacement level —
/// not a single subtraction against the Nth-best career total. A career total
/// conflates "good" with "young": a 22-year-old backup accumulates career value
/// through longevity alone, and measuring him against a career-level baseline
/// rewards him twice for the same fact. Computing the baseline year by year keeps
/// the age question where the aging curve already handles it.
/// </para>
/// </summary>
public static class PositionalValueCurveBuilder
{
    private static readonly string[] ScoredPositions = ["QB", "RB", "WR", "TE"];

    /// <summary>
    /// How many values to keep per position per year.
    ///
    /// <para>
    /// The deepest cutoff any realistic league produces is teams x startable slots
    /// at a position: a 16-team league playing 3 WR plus 2 flex reaches WR 80. 120
    /// clears that with room, and the cost is trivial — four positions times five
    /// projected years times 120 doubles is a few hundred kilobytes for the whole
    /// board.
    /// </para>
    ///
    /// <para>
    /// It is a truncation, not a limit on what can be asked. A cutoff past the
    /// stored depth is reported as such rather than silently clamped, because a
    /// clamped baseline reads as "the pool ran out" when the truth is "the curve
    /// was not stored deep enough" — and the first inflates every value above it.
    /// </para>
    /// </summary>
    public const int DefaultDepth = 120;

    /// <summary>
    /// Builds one curve per (projected year, position) from a set of career
    /// simulations.
    ///
    /// <para>
    /// Every simulation contributes each of its projected years to that year's
    /// curve. Simulations with no projections contribute nothing rather than a
    /// zero: a fabricated zero at the bottom of a curve is harmless, but a
    /// fabricated zero that lands <i>at</i> a cutoff becomes a replacement level of
    /// zero, and every value above it is then measured against nothing.
    /// </para>
    ///
    /// <para>
    /// Ties are left in whatever order the sort produces — unlike
    /// <see cref="ReplacementLevelService"/>, which breaks ties by player id for
    /// determinism, this keeps values only, so two equal values are
    /// indistinguishable and the ordering between them cannot affect any answer.
    /// </para>
    /// </summary>
    public static IReadOnlyList<PositionalValueCurve> Build(
        IReadOnlyList<CareerSimulationDocument> simulations,
        int depth = DefaultDepth)
    {
        ArgumentNullException.ThrowIfNull(simulations);
        if (depth <= 0)
            throw new ArgumentOutOfRangeException(nameof(depth), depth,
                "Curve depth must be positive — a zero-depth curve can answer no cutoff at all.");

        // (year, position) -> the season values contributed to it.
        var buckets = new Dictionary<(int Year, string Position), List<double>>();

        foreach (var sim in simulations)
        {
            if (sim.YearProjections.Count == 0) continue;

            var position = NormalizePosition(sim.Position);
            if (position is null) continue;

            foreach (var year in sim.YearProjections)
            {
                var key = (year.Year, position);
                if (!buckets.TryGetValue(key, out var values))
                {
                    values = [];
                    buckets[key] = values;
                }

                values.Add(year.SeasonValue);
            }
        }

        return buckets
            .OrderBy(b => b.Key.Year)
            .ThenBy(b => Array.IndexOf(ScoredPositions, b.Key.Position))
            .Select(b =>
            {
                var descending = b.Value
                    .OrderByDescending(v => v)
                    .Take(depth)
                    .ToList();

                return new PositionalValueCurve(
                    Year: b.Key.Year,
                    Position: b.Key.Position,
                    DescendingSeasonValues: descending,
                    PoolSize: b.Value.Count);
            })
            .ToList();
    }

    /// <summary>
    /// Case-insensitive match onto the four scored positions, or null for anything
    /// else. K and DEF fall out here — they carry no projections yet (FAN-124), so
    /// a curve for them would be an empty list wearing a position name.
    /// </summary>
    private static string? NormalizePosition(string position)
        => ScoredPositions.FirstOrDefault(
            p => string.Equals(p, position, StringComparison.OrdinalIgnoreCase));
}

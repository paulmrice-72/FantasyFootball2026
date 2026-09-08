namespace FF.Domain.Documents;

/// <summary>
/// The positional value curve for one projected season — FAN-153.
///
/// <para>
/// Replacement level is not a property of a player or of the model. It is a
/// property of a <b>league</b>: the projection of the first player at a position
/// who would not be starting anywhere in it. A twelve-team superflex league and a
/// ten-team single-QB league read the same projections and arrive at different
/// answers, and both are correct.
/// </para>
///
/// <para>
/// That is why this stores the <b>curve</b> rather than the level. For each
/// projected season and position it keeps the descending list of
/// <c>SeasonValue</c>s, so any roster configuration resolves its own replacement
/// level by indexing into it at its own cutoff. The expensive half — simulating
/// every career — stays in the nightly job; the league-specific half becomes an
/// array index at read time.
/// </para>
///
/// <para>
/// Persisted per (Season, ScoringFormat, Year, Position). <c>Season</c> is the base
/// season the simulations were run for; <c>Year</c> is the future season this
/// particular curve describes, so a five-year simulation produces five curves per
/// position.
/// </para>
/// </summary>
public class PositionalValueCurveDocument
{
    public string Id { get; set; } = string.Empty;

    /// <summary>Base season the career simulations were run for.</summary>
    public int Season { get; set; }

    /// <summary>
    /// Scoring format the simulations were produced under, as
    /// <c>ScoringFormat.ToString()</c>. Stored rather than assumed because the
    /// same league shape over different scoring produces different curves.
    /// </summary>
    public string ScoringFormat { get; set; } = string.Empty;

    /// <summary>The projected season this curve describes.</summary>
    public int Year { get; set; }

    /// <summary>QB, RB, WR or TE. K and DEF are not projected (FAN-124).</summary>
    public string Position { get; set; } = string.Empty;

    /// <summary>
    /// <c>SeasonValue</c> for every player at this position in this year, sorted
    /// descending and truncated to <see cref="Depth"/>.
    ///
    /// <para>
    /// Stored as <c>double</c> deliberately, matching
    /// <c>CareerYearProjection.SeasonValue</c>. Mongo stores this project's
    /// decimals as strings (FAN-127 / FAN-129), which makes server-side range
    /// filters on them unreliable; keeping the curve in the same numeric type as
    /// its source avoids inheriting that problem on a field that exists to be
    /// compared.
    /// </para>
    /// </summary>
    public List<double> DescendingSeasonValues { get; set; } = [];

    /// <summary>
    /// How many players existed at this position and year before truncation.
    ///
    /// <para>
    /// Kept because a cutoff past the end of the stored values has two very
    /// different causes: the league starts more players at this position than the
    /// pool contains (a real, if unusual, state), or the cutoff simply ran past
    /// <see cref="Depth"/> and the answer is retrievable with a deeper curve.
    /// Without this the two are indistinguishable and both look like an exhausted
    /// pool, which silently inflates every value above it.
    /// </para>
    /// </summary>
    public int PoolSize { get; set; }

    /// <summary>Number of values actually stored — <c>min(PoolSize, requested depth)</c>.</summary>
    public int Depth { get; set; }

    public DateTime ComputedAt { get; set; }
}

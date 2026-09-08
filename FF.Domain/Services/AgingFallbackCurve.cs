namespace FF.Domain.Services;

/// <summary>
/// The single source of truth for positional age windows and for the analytic
/// fallback multiplier used when no fitted aging curve covers an age.
///
/// <para>
/// FAN-157. There were two of these, in two projects, with the same intent and
/// different numbers: <c>AgingCurveService.GetDefaultMultiplier</c> ascended
/// from the position's window minimum (21/22), while
/// <c>CareerSimulationService.GetFallbackMultiplier</c> ascended from a
/// hardcoded 18. The same 23-year-old running back got a different aging
/// multiplier depending on which service asked. Neither was wrong on its own
/// terms; having both was.
/// </para>
///
/// <para>
/// The fallback is not a model. It is a smooth, obviously-shaped stand-in that
/// exists so a missing curve degrades to something sane rather than to zero or
/// to a crash. When it is doing the work, that is a fact worth surfacing, not a
/// success.
/// </para>
/// </summary>
public static class AgingFallbackCurve
{
    /// <param name="MinAge">Floor of the modelling window — the youngest age with enough data to fit.</param>
    /// <param name="PeakAge">Age at which the position is assumed to peak.</param>
    /// <param name="MaxAge">Ceiling of the modelling window. Bounds curve fitting and the stored age map.</param>
    /// <param name="DeclineWindow">
    /// Years after the peak over which the fallback decays to its floor. Kept
    /// separate from <paramref name="MaxAge"/> deliberately: the modelling
    /// window is "where we have data", the decline window is "how fast this
    /// position falls off", and conflating them made RB's decline depend on how
    /// far the data happened to extend.
    /// </param>
    public readonly record struct AgeWindow(int MinAge, int PeakAge, int MaxAge, double DeclineWindow);

    /// <summary>
    /// Peak ages match the values <c>CareerSimulationService.PeakAges</c> has
    /// always used; decline windows match its <c>PostPeakWindow</c>. Min/Max
    /// match the fitting windows <c>AgingCurveService</c> has always used. No
    /// shape changes here — this consolidates four dictionaries across two
    /// files into one table, so the next tuning pass has one place to edit.
    /// </summary>
    private static readonly Dictionary<string, AgeWindow> Windows = new(StringComparer.OrdinalIgnoreCase)
    {
        ["QB"] = new(MinAge: 22, PeakAge: 29, MaxAge: 40, DeclineWindow: 8.0),
        ["RB"] = new(MinAge: 21, PeakAge: 24, MaxAge: 32, DeclineWindow: 5.0),
        ["WR"] = new(MinAge: 21, PeakAge: 26, MaxAge: 35, DeclineWindow: 9.0),
        ["TE"] = new(MinAge: 21, PeakAge: 27, MaxAge: 35, DeclineWindow: 8.0)
    };

    private static readonly AgeWindow Default = new(MinAge: 21, PeakAge: 26, MaxAge: 35, DeclineWindow: 8.0);

    public static AgeWindow WindowFor(string position) =>
        Windows.GetValueOrDefault(position ?? string.Empty, Default);

    public static IEnumerable<string> ModelledPositions => Windows.Keys;

    /// <summary>
    /// Asymmetric bell: a gentle ascent to the peak, a quadratic decline after
    /// it. Returns a multiplier in [0.1, 1.0].
    ///
    /// <para>
    /// The ascent base is the window minimum, not 18. That is the choice being
    /// made between the two old implementations, and it is the coherent one —
    /// the window minimum is by definition the youngest age this position is
    /// modelled at, so the ascent should start there rather than at an age no
    /// window mentions. Ages below the window clamp to the window floor instead
    /// of extrapolating to a negative ascent.
    /// </para>
    /// </summary>
    public static double Multiplier(string position, int age)
    {
        var w = WindowFor(position);

        // Clamp rather than extrapolate. Outside the window there is no data
        // behind any answer, and a smooth extrapolation of a made-up curve just
        // dresses that up.
        var a = Math.Clamp(age, w.MinAge, w.MaxAge);

        if (a <= w.PeakAge)
        {
            var span = Math.Max(1, w.PeakAge - w.MinAge);
            var ascent = (double)(a - w.MinAge) / span;
            return 0.6 + 0.4 * ascent;
        }

        var decline = (a - w.PeakAge) / Math.Max(1.0, w.DeclineWindow);
        return Math.Max(0.1, 1.0 - 0.9 * decline * decline);
    }

    /// <summary>
    /// Rejects a fitted age→value series that cannot be an aging curve.
    ///
    /// <para>
    /// FAN-157's failure signature was a curve that <em>rises monotonically
    /// with age</em> — built cross-sectionally, it measured which players
    /// survive to each age rather than how a player changes, and survivorship
    /// points the wrong way. It was stored and served for a day without
    /// anything objecting, and the symptom that reached the UI was a 23-year-old
    /// Malik Nabers showing zero prime years remaining while a 27-year-old
    /// CeeDee Lamb showed four.
    /// </para>
    ///
    /// <para>
    /// Two properties catch it. The peak must sit strictly inside the window —
    /// a curve peaking at its own last age is monotonic ascent with the top
    /// chopped off, which is the exact defect. And the series must be
    /// single-peaked: rising to the peak, falling after it. Real aging is one
    /// hump; a wiggly degree-3 fit that recovers at 34 is fitting noise.
    /// </para>
    ///
    /// <para>
    /// <paramref name="tolerance"/> permits tiny non-monotonic steps from
    /// floating-point noise and rounding without permitting a real second hump.
    /// </para>
    /// </summary>
    /// <param name="minPeakInset">
    /// How far inside the window the peak must sit. One is not enough: the first
    /// live run produced a WR curve peaking at 22 against a window floor of 21,
    /// which passed a strict "not at the edge" test while being, in substance,
    /// the same monotonic decline that got QB and RB rejected outright. A peak
    /// one step from the floor is a declining curve with a bump on the end.
    /// </param>
    public static bool IsPlausibleAgingCurve(
        IReadOnlyDictionary<int, double> ageValueMap,
        int minAge,
        int maxAge,
        out string failureReason,
        double tolerance = 0.5,
        int minPeakInset = 2)
    {
        failureReason = string.Empty;

        var ages = ageValueMap.Keys.Where(a => a >= minAge && a <= maxAge).OrderBy(a => a).ToList();
        if (ages.Count < 3)
        {
            failureReason = $"only {ages.Count} ages in [{minAge}, {maxAge}]";
            return false;
        }

        var peakAge = ages.MaxBy(a => ageValueMap[a]);

        if (peakAge < minAge + minPeakInset || peakAge > maxAge - minPeakInset)
        {
            failureReason =
                $"peak age {peakAge} is within {minPeakInset} of the edge of the window " +
                $"[{minAge}, {maxAge}] — a curve peaking at or beside its first or last age " +
                "is monotonic, which is a survivorship or filtering artefact, not aging";
            return false;
        }

        for (var i = 1; i < ages.Count; i++)
        {
            var prev = ageValueMap[ages[i - 1]];
            var cur = ageValueMap[ages[i]];
            var rising = ages[i] <= peakAge;

            if (rising && cur < prev - tolerance)
            {
                failureReason =
                    $"value falls from age {ages[i - 1]} ({prev:F1}) to {ages[i]} ({cur:F1}) " +
                    $"before the peak at {peakAge} — not single-peaked";
                return false;
            }

            if (!rising && cur > prev + tolerance)
            {
                failureReason =
                    $"value rises from age {ages[i - 1]} ({prev:F1}) to {ages[i]} ({cur:F1}) " +
                    $"after the peak at {peakAge} — not single-peaked";
                return false;
            }
        }

        return true;
    }
}

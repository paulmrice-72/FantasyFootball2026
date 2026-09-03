// FF.Application/Services/RosterStrengthCalculator.cs
using FF.Domain.Entities;

namespace FF.Application.Services;

/// <summary>
/// Shared "roster strength" calculation, plus the generic within-league
/// percentile ranking and letter-grade mapping, used by both the dynasty
/// (GetLeagueRosterGradesQueryHandler) and redraft
/// (GetRedraftRosterGradesQueryHandler) Roster Grades handlers.
///
/// FAN-107 (2026-08-30): extracted out of GetLeagueRosterGradesQueryHandler
/// so the two handlers can't drift into two separate depth-scoring
/// implementations over time. FAN-108 plans to reuse RankByDescending /
/// RankFractionToGrade again for the Standings pre-season tiebreaker.
///
/// 2026-09-01: the per-position breakdown behind the overall score is now
/// exposed (<see cref="ComputePositionStrengths"/>) so the Standings table can
/// show a positional grade per team without a round-trip per team, and both the
/// overall score and the breakdown are computed from one implementation.
///
/// NAMING CAUTION: this is a STARTERS score, not a depth score. It looks only at
/// each position's best N players (N = starter slots) and ignores the bench
/// entirely. That is a different measure from
/// GetPositionalDepthGradesQueryHandler, which does weight bench players. Two
/// pages can legitimately disagree about the same roster because they are
/// answering two different questions.
/// </summary>
public static class RosterStrengthCalculator
{
    private static readonly Dictionary<string, double> PositionBaseline = new()
    {
        ["QB"] = 19.3,
        ["RB"] = 15.1,
        ["WR"] = 13.1,
        ["TE"] = 12.1
    };

    private static readonly Dictionary<string, int> StarterSlots = new()
    {
        ["QB"] = 1,
        ["RB"] = 2,
        ["WR"] = 3,
        ["TE"] = 1
    };

    public static readonly string[] GradedPositions = ["QB", "RB", "WR", "TE"];

    /// <summary>
    /// Positional baseline used to normalise sim medians — roughly a startable
    /// starter's per-game production at that position. Exposed so callers can
    /// compare players across positions without re-declaring the numbers.
    /// </summary>
    public static double GetBaseline(string position) =>
        PositionBaseline.TryGetValue(position, out var b) ? b : 0.0;

    /// <summary>One position's contribution to a roster's overall strength.</summary>
    /// <param name="StarterPoints">Average sim median of the best N at the position.</param>
    /// <param name="NormalizedScore">
    /// StarterPoints against the positional baseline, scaled to a 50-point axis.
    /// Zero when starter quality is below half of baseline (GRADE-FIX-002).
    /// </param>
    public readonly record struct PositionStrength(
        string Position,
        double StarterPoints,
        double NormalizedScore,
        int RosteredCount = 0,
        int ProjectedCount = 0);

    /// <summary>
    /// Per-position starter strength for one roster. This is the breakdown that
    /// <see cref="ComputeRawDepthScore"/> averages, so a team's overall number and
    /// its positional numbers can never disagree.
    /// </summary>
    public static IReadOnlyList<PositionStrength> ComputePositionStrengths(
        IEnumerable<string> rosterPlayerIds,
        IReadOnlyDictionary<string, Player> playerLookup,
        IReadOnlyDictionary<string, double> simMedianLookup)
    {
        var playerIds = rosterPlayerIds as ICollection<string> ?? rosterPlayerIds.ToList();
        var result = new List<PositionStrength>(GradedPositions.Length);

        foreach (var pos in GradedPositions)
        {
            var baseline = PositionBaseline[pos];
            var slots = StarterSlots[pos];

            var rosteredAtPosition = playerIds
                .Where(id =>
                {
                    playerLookup.TryGetValue(id, out var p);
                    return p?.Position.ToString() == pos;
                })
                .ToList();

            // A player with no simulation row is UNKNOWN, not zero. The previous
            // version mapped a lookup miss to 0.0 and then averaged it in, so a
            // roster carrying an unprojected starter was pushed down the league
            // table by a number nobody had produced. Verified 2026-09-02: Kenneth
            // Walker had no reachable sim row in any season, and this is the same
            // fabricated-zero pattern FAN-124 removed from the lineup card and
            // GetPositionalDepthGradesQueryHandler.
            //
            // Excluding him is not free either — a position judged on fewer players
            // is judged on less evidence — so the counts travel with the result and
            // the UI says when a placing rests on an incomplete room.
            var projected = rosteredAtPosition
                .Select(id => simMedianLookup.TryGetValue(id, out var m) ? (double?)m : null)
                .Where(m => m is > 0)
                .Select(m => m!.Value)
                .OrderByDescending(m => m)
                .ToList();

            var starterScore = projected.Take(slots).DefaultIfEmpty(0).Average();

            // GRADE-FIX-002: a position contributes 0 if its starter quality is
            // below 50% of baseline — keeps a handful of filler players from
            // inflating a thin position's grade.
            var starterQualityFloor = baseline * 0.50;
            var normalized = starterScore >= starterQualityFloor && baseline > 0
                ? Math.Clamp((starterScore / baseline) * 50.0, 0, 100)
                : 0.0;

            result.Add(new PositionStrength(
                pos, starterScore, normalized,
                RosteredCount: rosteredAtPosition.Count,
                ProjectedCount: projected.Count));
        }

        return result;
    }

    /// <summary>
    /// Raw roster score (0-100, not yet league-relative) for one roster: the
    /// average across QB/RB/WR/TE of each position's normalised starter quality.
    /// </summary>
    public static double ComputeRawDepthScore(
        IEnumerable<string> rosterPlayerIds,
        IReadOnlyDictionary<string, Player> playerLookup,
        IReadOnlyDictionary<string, double> simMedianLookup)
    {
        var strengths = ComputePositionStrengths(rosterPlayerIds, playerLookup, simMedianLookup);
        return strengths.Count > 0 ? strengths.Sum(s => s.NormalizedScore) / strengths.Count : 0.0;
    }

    /// <summary>
    /// Ranks items by a score (descending — best first) and returns each
    /// item's rank fraction in the SAME ORDER as the input list: 0.0 = best
    /// in the league, 1.0 = worst. Ties share the same fraction (average
    /// rank) so two teams with an identical score get the same grade
    /// instead of an arbitrary tiebreak deciding one is a letter grade
    /// better than the other.
    /// </summary>
    public static double[] RankByDescending<T>(List<T> items, Func<T, double> selector)
    {
        var n = items.Count;
        var fractions = new double[n];
        if (n <= 1) return fractions; // single team in the league — no basis for relative grading

        var order = Enumerable.Range(0, n)
            .OrderByDescending(i => selector(items[i]))
            .ToList();

        var i = 0;
        while (i < n)
        {
            var j = i;
            while (j < n && selector(items[order[j]]).Equals(selector(items[order[i]]))) j++;
            var avgRank = (i + j - 1) / 2.0; // 0-based average rank across the tied group
            var fraction = avgRank / (n - 1);
            for (var k = i; k < j; k++) fractions[order[k]] = fraction;
            i = j;
        }

        return fractions;
    }

    /// <summary>
    /// Integer placing (1 = best) for each item, in the SAME ORDER as the input
    /// list. Ties share the better placing. Used for the per-position "3rd of 12"
    /// figure, which is far more legible than a letter when the underlying scores
    /// are tightly bunched.
    /// </summary>
    public static int[] PlacingByDescending<T>(List<T> items, Func<T, double> selector)
    {
        var n = items.Count;
        var placings = new int[n];
        if (n == 0) return placings;

        var order = Enumerable.Range(0, n)
            .OrderByDescending(i => selector(items[i]))
            .ToList();

        var i = 0;
        while (i < n)
        {
            var j = i;
            while (j < n && selector(items[order[j]]).Equals(selector(items[order[i]]))) j++;
            for (var k = i; k < j; k++) placings[order[k]] = i + 1;
            i = j;
        }

        return placings;
    }

    /// <summary>
    /// Maps a within-league rank fraction (0.0 = best team in the league,
    /// 1.0 = worst) to a letter grade. Percentile-within-league is
    /// self-correcting — it always reflects "how does my team compare to
    /// the ~10-14 others in THIS league" and never needs recalibrating when
    /// the underlying score scale moves (see FAN-95 follow-up notes on the
    /// old fixed-cutoff approach this replaced).
    ///
    /// The trade-off worth remembering: because this is purely relative, a league
    /// of twelve near-identical rosters still produces an A+ and an F. The letter
    /// says where you place, not how good you are.
    /// </summary>
    public static string RankFractionToGrade(double rankFraction) => rankFraction switch
    {
        <= 0.08 => "A+",
        <= 0.20 => "A",
        <= 0.35 => "B+",
        <= 0.50 => "B",
        <= 0.65 => "C+",
        <= 0.80 => "C",
        <= 0.92 => "D",
        _ => "F"
    };
}

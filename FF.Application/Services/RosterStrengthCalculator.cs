// FF.Application/Services/RosterStrengthCalculator.cs
using FF.Domain.Entities;

namespace FF.Application.Services;

/// <summary>
/// Shared "roster strength" (Depth Score) calculation, plus the generic
/// within-league percentile ranking and letter-grade mapping, used by both
/// the dynasty (GetLeagueRosterGradesQueryHandler) and redraft
/// (GetRedraftRosterGradesQueryHandler) Roster Grades handlers.
///
/// FAN-107 (2026-08-30): extracted out of GetLeagueRosterGradesQueryHandler
/// so the two handlers can't drift into two separate depth-scoring
/// implementations over time. FAN-108 plans to reuse RankByDescending /
/// RankFractionToGrade again for the Standings pre-season tiebreaker.
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

    /// <summary>
    /// Raw Depth Score (0-100, not yet league-relative) for one roster:
    /// average, across QB/RB/WR/TE, of each position's starter-quality
    /// (best N sim-median players at that position, N = starter slots)
    /// normalised against a fixed positional baseline.
    ///
    /// GRADE-FIX-002: a position contributes 0 if its starter quality is
    /// below 50% of baseline — keeps a handful of filler players from
    /// inflating a thin position's grade.
    /// </summary>
    public static double ComputeRawDepthScore(
        IEnumerable<string> rosterPlayerIds,
        IReadOnlyDictionary<string, Player> playerLookup,
        IReadOnlyDictionary<string, double> simMedianLookup)
    {
        var playerIds = rosterPlayerIds as ICollection<string> ?? rosterPlayerIds.ToList();
        double totalDepthScore = 0;
        var positionsGraded = 0;

        foreach (var pos in new[] { "QB", "RB", "WR", "TE" })
        {
            var baseline = PositionBaseline[pos];
            var slots = StarterSlots[pos];

            var posPlayers = playerIds
                .Where(id =>
                {
                    playerLookup.TryGetValue(id, out var p);
                    return p?.Position.ToString() == pos;
                })
                .Select(id => simMedianLookup.TryGetValue(id, out var m) ? m : 0.0)
                .OrderByDescending(m => m)
                .ToList();

            var starterScore = posPlayers.Take(slots).DefaultIfEmpty(0).Average();

            var starterQualityFloor = baseline * 0.50;
            if (starterScore >= starterQualityFloor)
            {
                var starterNorm = baseline > 0 ? (starterScore / baseline) * 50.0 : 0;
                totalDepthScore += Math.Clamp(starterNorm, 0, 100);
            }
            positionsGraded++;
        }

        return positionsGraded > 0 ? totalDepthScore / positionsGraded : 0.0;
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
    /// Maps a within-league rank fraction (0.0 = best team in the league,
    /// 1.0 = worst) to a letter grade. Percentile-within-league is
    /// self-correcting — it always reflects "how does my team compare to
    /// the ~10-14 others in THIS league" and never needs recalibrating when
    /// the underlying score scale moves (see FAN-95 follow-up notes on the
    /// old fixed-cutoff approach this replaced).
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

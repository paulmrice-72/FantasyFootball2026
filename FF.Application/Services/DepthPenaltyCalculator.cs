// FF.Application/Services/DepthPenaltyCalculator.cs
using FF.Domain.Documents;

namespace FF.Application.Services;

/// <summary>
/// Computes a dynasty trade value penalty multiplier for TE and RB players
/// who are depth-chart backups. Starters and non-TE/RB positions return 1.0.
///
/// Penalty table:
///   TE depth 2  → 0.50 (blocked behind TE1)
///   TE depth 3+ → 0.25 (deep backup)
///   RB depth 2  → 0.70 (committee backs still get touches)
///   RB depth 3+ → 0.40 (3rd RB rarely contributes)
///
/// Age gate (TE only): if the blocking TE1 is age 28+, the penalty is softened
/// halfway toward 1.0 — the blocker may age out soon, giving the backup more upside.
/// </summary>
public static class DepthPenaltyCalculator
{
    public static double ComputeDepthPenalty(
        string sleeperPlayerId,
        string position,
        Dictionary<string, DepthChartDocument> depthLookup,
        Dictionary<string, int?> te1AgeByTeam)
    {
        if (position != "TE" && position != "RB") return 1.0;
        if (!depthLookup.TryGetValue(sleeperPlayerId, out var doc)) return 1.0;

        var depthSlot = doc.DepthTeam;
        if (depthSlot <= 1) return 1.0;

        double basePenalty = (position, depthSlot) switch
        {
            ("TE", 2) => 0.50,
            ("TE", >= 3) => 0.25,
            ("RB", 2) => 0.70,
            ("RB", >= 3) => 0.40,
            _ => 1.00
        };

        // Age gate for TE only — soften penalty if TE1 is 28+
        if (position == "TE" && depthSlot >= 2)
        {
            var te1Age = te1AgeByTeam.TryGetValue(doc.NflTeam, out var age) ? age : null;
            if (te1Age.HasValue && te1Age.Value >= 28)
                basePenalty = basePenalty + ((1.0 - basePenalty) * 0.50);
        }

        return basePenalty;
    }

    public static Dictionary<string, int?> BuildTe1AgeByTeam(
        IReadOnlyList<DepthChartDocument> depthDocs,
        IReadOnlyList<Domain.Entities.Player> players)
    {
        var playerLookup = players
            .Where(p => p.SleeperPlayerId != null)
            .ToDictionary(p => p.SleeperPlayerId!, p => p);

        return depthDocs
            .Where(d => d.Position == "TE" && d.DepthTeam == 1)
            .GroupBy(d => d.NflTeam)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    playerLookup.TryGetValue(g.First().SleeperPlayerId, out var p);
                    return p?.Age;
                });
    }
}
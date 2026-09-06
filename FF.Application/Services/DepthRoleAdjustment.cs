// FF.Application/Services/DepthRoleAdjustment.cs
namespace FF.Application.Services;

/// <summary>
/// Applies a depth-chart role term to a projection (2026-09-07, part of FAN-137).
///
/// WHY THIS EXISTS
///
/// A projection regressed from last season's game logs describes the role a
/// player HAD. Nothing in the engine asked whether he still has it. That is how
/// Jacoby Brissett — a 2025 starter, a 2026 backup — projected as QB13 at 17.44
/// points, and it is the general shape of the "Joe Milton" complaint: a
/// quarterback with no path to snaps ranked as though he had one.
///
/// Two guardrails should have caught this and neither could:
///
///   * <see cref="DepthPenaltyCalculator"/> returns 1.0 for anything that is not
///     a TE or an RB, so a quarterback was never subject to it by construction.
///   * <see cref="RoleClassificationService.ClassifyQb"/> keyed off
///     <c>SnapPctSeason</c>, which was identically zero for every player until
///     the snap-count name-join was fixed (FAN-141). It returned
///     <c>Unknown</c> for every QB ever scored — confirmed in the 2026 data,
///     where every simulation row carries <c>PlayerRole: "Unknown"</c>.
///
/// SCOPE — QUARTERBACKS ONLY, DELIBERATELY
///
/// QB is the position where depth is nearly deterministic: one man takes the
/// snaps and the backup takes none. RB, WR and TE are committees whose real
/// usage is already encoded in the game logs the projection regresses from, and
/// a preseason depth chart is a noisy thing to overrule them with. Extending
/// this to those positions is a separate decision with its own evidence, not a
/// free generalization.
///
/// ON DOUBLE-COUNTING
///
/// A player who was ALREADY a backup last season has a per-game line that
/// already reflects backup usage, so gating him again is mildly conservative —
/// Joe Milton's 4.55 becomes 1.14 when arguably it should just stay 4.55. That
/// is an acceptable error: he belongs near the floor either way. The case this
/// exists for is the opposite one, where last season's line is a STARTER's line
/// and this season's role is not, and there the gate is the only thing standing
/// between a backup and a top-15 ranking.
///
/// The multiplier and the resolved role are both stamped onto the projection
/// document. A number this large must be explainable from the stored row alone —
/// silently adjusting a projection is how you get a ranking nobody can defend.
/// </summary>
public static class DepthRoleAdjustment
{
    /// <summary>Applied when no depth-chart row exists for the player.</summary>
    public const string UnknownDepthRole = "UnknownDepth";

    /// <summary>Applied to every position this gate deliberately does not touch.</summary>
    public const string NotGatedRole = "NotGated";

    /// <summary>
    /// Resolves the role multiplier for a player. Returns 1.0 and a label for
    /// anything outside the gate's scope, so the caller can apply the result
    /// unconditionally without branching on position itself.
    /// </summary>
    public static (decimal Multiplier, string Role) Resolve(string? position, int? depthTeam)
    {
        if (string.IsNullOrWhiteSpace(position))
            return (1.00m, NotGatedRole);

        return position.Trim().ToUpperInvariant() switch
        {
            "QB" => ResolveQuarterback(depthTeam),
            _ => (1.00m, NotGatedRole)
        };
    }

    /// <summary>
    /// A missing depth row returns 1.0 rather than a penalty. Absence of evidence
    /// is not evidence of being a backup, and the 2026 chart carries 34 QBs at
    /// depth 1 against 32 teams — complete enough that a missing row means the
    /// player is off the chart entirely, not that he is buried on it.
    /// </summary>
    private static (decimal Multiplier, string Role) ResolveQuarterback(int? depthTeam)
    {
        if (depthTeam is null || depthTeam <= 0)
            return (1.00m, UnknownDepthRole);

        return depthTeam.Value switch
        {
            1 => (1.00m, "StartingQB"),

            // A QB2 is one injury from relevance and zero snaps from irrelevance.
            // 0.25 keeps him orderable against other backups instead of flattening
            // every one of them to the same number, which is the failure mode the
            // dynasty tier caps hit repeatedly (see DfvCalculationService).
            2 => (0.25m, "BackupQB"),

            _ => (0.10m, "ThirdStringQB")
        };
    }
}

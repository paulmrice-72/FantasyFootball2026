// FF.Domain/ValueObjects/ProjectedStatLine.cs
namespace FF.Domain.ValueObjects;

/// <summary>
/// L0 output of the Unified Projection Engine (Epic 20 / FAN-116).
///
/// An expected stat line for ONE game, expressed in football units — not fantasy
/// points. Fantasy points are derived at the edge (L1) by applying a league's own
/// scoring settings, which is what makes the core format-agnostic.
///
/// All values are expected values, so they are fractional by design
/// (e.g. 6.8 targets, 0.42 receiving TDs). They are CONDITIONAL ON PLAYING —
/// availability is carried separately in <see cref="AvailabilityRate"/> so that
/// per-game consumers (start/sit, matchup) and season-total consumers (rankings,
/// roster grades) can each apply it correctly rather than having it baked in.
/// </summary>
public class ProjectedStatLine
{
    // ── Passing ───────────────────────────────────────────────────────────
    public decimal PassingAttempts { get; set; }
    public decimal Completions { get; set; }
    public decimal PassingYards { get; set; }
    public decimal PassingTds { get; set; }
    public decimal Interceptions { get; set; }

    // ── Rushing ───────────────────────────────────────────────────────────
    public decimal Carries { get; set; }
    public decimal RushingYards { get; set; }
    public decimal RushingTds { get; set; }

    // ── Receiving ─────────────────────────────────────────────────────────
    public decimal Targets { get; set; }
    public decimal Receptions { get; set; }
    public decimal ReceivingYards { get; set; }
    public decimal ReceivingTds { get; set; }

    // ── Misc ──────────────────────────────────────────────────────────────
    public decimal FumblesLost { get; set; }
    public decimal TwoPointConversions { get; set; }
    public decimal SpecialTeamsTds { get; set; }

    /// <summary>
    /// Probability the player is active for any given game, derived from the
    /// share of the basis window in which he actually played. 1.0 = never missed.
    /// Multiply a per-game line by this when building season totals.
    /// </summary>
    public decimal AvailabilityRate { get; set; } = 1.0m;

    /// <summary>Total offensive opportunities (attempts + carries + targets) per game.</summary>
    public decimal TotalOpportunities => PassingAttempts + Carries + Targets;

    public bool IsEmpty => TotalOpportunities <= 0m;

    public static ProjectedStatLine Empty => new();

    /// <summary>
    /// Returns a copy with every counting stat multiplied by <paramref name="factor"/>.
    /// Used to convert a per-game line into a season total, or to apply an
    /// availability-weighted expectation. Rates are unaffected because this scales
    /// both volume and the yardage/TD totals derived from it.
    /// </summary>
    public ProjectedStatLine Scale(decimal factor) => new()
    {
        PassingAttempts = PassingAttempts * factor,
        Completions = Completions * factor,
        PassingYards = PassingYards * factor,
        PassingTds = PassingTds * factor,
        Interceptions = Interceptions * factor,
        Carries = Carries * factor,
        RushingYards = RushingYards * factor,
        RushingTds = RushingTds * factor,
        Targets = Targets * factor,
        Receptions = Receptions * factor,
        ReceivingYards = ReceivingYards * factor,
        ReceivingTds = ReceivingTds * factor,
        FumblesLost = FumblesLost * factor,
        TwoPointConversions = TwoPointConversions * factor,
        SpecialTeamsTds = SpecialTeamsTds * factor,
        AvailabilityRate = AvailabilityRate
    };

    /// <summary>Rounds every value for storage/display readability.</summary>
    public ProjectedStatLine Rounded(int decimals = 3) => new()
    {
        PassingAttempts = Math.Round(PassingAttempts, decimals),
        Completions = Math.Round(Completions, decimals),
        PassingYards = Math.Round(PassingYards, decimals),
        PassingTds = Math.Round(PassingTds, decimals),
        Interceptions = Math.Round(Interceptions, decimals),
        Carries = Math.Round(Carries, decimals),
        RushingYards = Math.Round(RushingYards, decimals),
        RushingTds = Math.Round(RushingTds, decimals),
        Targets = Math.Round(Targets, decimals),
        Receptions = Math.Round(Receptions, decimals),
        ReceivingYards = Math.Round(ReceivingYards, decimals),
        ReceivingTds = Math.Round(ReceivingTds, decimals),
        FumblesLost = Math.Round(FumblesLost, decimals),
        TwoPointConversions = Math.Round(TwoPointConversions, decimals),
        SpecialTeamsTds = Math.Round(SpecialTeamsTds, decimals),
        AvailabilityRate = Math.Round(AvailabilityRate, decimals)
    };
}

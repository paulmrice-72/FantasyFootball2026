// FF.Domain/ValueObjects/LeagueScoringSettings.cs
namespace FF.Domain.ValueObjects;

/// <summary>
/// L1 input of the Unified Projection Engine (Epic 20 / FAN-116).
///
/// The complete scoring rule set for one league. A <see cref="ProjectedStatLine"/>
/// plus one of these produces fantasy points — which is why the engine never needs
/// a per-format projection column. Adding a format is adding one of these, not a
/// new field on the projection document.
///
/// Defaults match the Sleeper defaults the app already syncs into
/// <c>League.RecPerReception</c> / <c>PassingTdPoints</c> / <c>BonusRecTe</c>.
/// The remaining values are league-invariant in every format we currently support;
/// when Sleeper's full scoring_settings block is synced (PROJ-003 follow-up) they
/// become per-league too, with no change to any consumer.
/// </summary>
public record LeagueScoringSettings
{
    // ── Passing ───────────────────────────────────────────────────────────
    public decimal PointsPerPassingYard { get; init; } = 0.04m;
    public decimal PassingTdPoints { get; init; } = 4m;
    public decimal InterceptionPoints { get; init; } = -2m;

    // ── Rushing ───────────────────────────────────────────────────────────
    public decimal PointsPerRushingYard { get; init; } = 0.1m;
    public decimal RushingTdPoints { get; init; } = 6m;

    // ── Receiving ─────────────────────────────────────────────────────────
    /// <summary>PPR value. 0 = standard, 0.5 = half-PPR, 1.0 = full PPR.</summary>
    public decimal PointsPerReception { get; init; } = 1m;
    public decimal PointsPerReceivingYard { get; init; } = 0.1m;
    public decimal ReceivingTdPoints { get; init; } = 6m;
    /// <summary>TE premium — extra points per reception, TE only.</summary>
    public decimal BonusRecTe { get; init; } = 0m;

    // ── Misc ──────────────────────────────────────────────────────────────
    public decimal FumbleLostPoints { get; init; } = -2m;
    public decimal TwoPointConversionPoints { get; init; } = 2m;
    public decimal SpecialTeamsTdPoints { get; init; } = 6m;

    // ── Presets ───────────────────────────────────────────────────────────
    // These exist so the projection job can cache a points value for the three
    // common formats. Real league scoring always comes from League.GetScoringSettings().

    public static LeagueScoringSettings Standard => new() { PointsPerReception = 0m };
    public static LeagueScoringSettings HalfPpr => new() { PointsPerReception = 0.5m };
    public static LeagueScoringSettings FullPpr => new() { PointsPerReception = 1m };

    /// <summary>
    /// Builds a rule set from the three scoring fields currently synced from Sleeper.
    /// Prefer <c>League.GetScoringSettings()</c> over calling this directly.
    /// </summary>
    public static LeagueScoringSettings From(
        decimal recPerReception,
        decimal passingTdPoints,
        decimal bonusRecTe = 0m) => new()
        {
            PointsPerReception = recPerReception,
            PassingTdPoints = passingTdPoints,
            BonusRecTe = bonusRecTe
        };

    /// <summary>
    /// Best-effort mapping from the legacy <c>ScoringFormat</c> enum, for call sites
    /// that have a format label but no League. Superflex variants affect roster shape
    /// (L3), not scoring, so they map to their PPR equivalent here.
    /// </summary>
    public static LeagueScoringSettings FromFormatName(string? formatName) =>
        (formatName ?? string.Empty).ToLowerInvariant() switch
        {
            "standard" => Standard,
            "fullppr" or "ppr" or "superflexfullppr" => FullPpr,
            _ => HalfPpr
        };
}

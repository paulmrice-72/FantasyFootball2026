// FF.Domain/Documents/VorpRecommendationDocument.cs

namespace FF.Domain.Documents;

/// <summary>
/// One player's value over replacement, for one league, season and week.
/// Collection: vorp_recommendations.
///
/// <para>
/// <b>Scoped to a league by design (FAN-118).</b> This document previously keyed on
/// PlayerId/Season/Week alone, which made league-aware VORP impossible to express:
/// both baselines below depend on the league — the structural one on its roster
/// configuration, the free-agent one on who its members have rostered. The same
/// player is worth a different amount in a superflex league than a 1QB one, and
/// that is the whole point of computing this.
/// </para>
/// </summary>
public class VorpRecommendationDocument
{
    public string? Id { get; set; }

    /// <summary>The league these numbers were computed for. Part of the unique key.</summary>
    public string SleeperLeagueId { get; set; } = string.Empty;

    public string PlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string? NflTeam { get; set; }
    public int Season { get; set; }
    public int Week { get; set; }

    /// <summary>
    /// Whether anyone in this league rostered the player when the board was computed.
    /// Both are stored: rankings and Top Assets need the whole pool, the waiver board
    /// needs only the players you could actually add. Without this the two cannot be
    /// told apart after the fact — VorpFreeAgent is populated for rostered players too.
    /// </summary>
    public bool IsRostered { get; set; }

    /// <summary>
    /// Scored from the projection's stat line through the L1 scoring service using
    /// THIS league's settings — not read from a cached point column, which is what
    /// makes the number correct for full-PPR, TE-premium and the rest (FAN-97).
    /// </summary>
    public decimal ProjectedPoints { get; set; }

    /// <summary>
    /// From the Monte Carlo distribution, when a simulation exists for this player.
    /// Null rather than zero when it does not — a floor of 0.0 and no floor at all
    /// are different claims, and only one of them is true.
    /// </summary>
    public decimal? FloorPoints { get; set; }

    /// <summary>See <see cref="FloorPoints"/>.</summary>
    public decimal? CeilingPoints { get; set; }

    // ── Baseline 1: structural ────────────────────────────────────────────
    /// <summary>
    /// Projection of the first player at this position who would not be starting
    /// anywhere in the league, given its roster configuration. Stable week to week
    /// and comparable across positions — the right denominator for rankings, trade
    /// value and Top Assets.
    /// </summary>
    public decimal ReplacementLevel { get; set; }

    /// <summary><see cref="ProjectedPoints"/> − <see cref="ReplacementLevel"/>.</summary>
    public decimal Vorp { get; set; }

    // ── Baseline 2: free agent ────────────────────────────────────────────
    /// <summary>
    /// Projection of the best player at this position nobody in the league rosters.
    /// Answers the waiver question rather than the ranking one. Null when the
    /// position has no free agents at all — which is a real state, and distinct from
    /// a baseline of zero.
    /// </summary>
    public decimal? ReplacementLevelFreeAgent { get; set; }

    /// <summary>
    /// <see cref="ProjectedPoints"/> − <see cref="ReplacementLevelFreeAgent"/>, computed
    /// leave-one-out: the best free agent is measured against the second best rather
    /// than himself, so the top waiver target does not always score exactly zero.
    /// </summary>
    public decimal? VorpFreeAgent { get; set; }

    /// <summary>
    /// True when the league starts more players at this position than there are
    /// projections for, so <see cref="ReplacementLevel"/> fell back to the last real
    /// projection rather than a fabricated zero. Surfaces as a caveat in the UI.
    /// </summary>
    public bool ReplacementPoolExhausted { get; set; }

    /// <summary>Rank by <see cref="Vorp"/> across all positions, within this league/week.</summary>
    public int VorpRank { get; set; }

    /// <summary>Rank by <see cref="Vorp"/> within this position.</summary>
    public int PositionRank { get; set; }

    public DateTime ComputedAt { get; set; }
}

// FF.Domain/Documents/PlayerProjectionDocument.cs
using FF.Domain.ValueObjects;

namespace FF.Domain.Documents;

/// <summary>
/// One player's projection for one season/week. Collection: player_projections.
///
/// Epic 20 schema decision: the STAT LINE is canonical. The ProjectedPoints*
/// fields below are a denormalized cache of the three common formats, kept only
/// so existing readers keep working and so simple list queries can sort without
/// re-scoring. Anything that needs a league's real format must score
/// <see cref="StatLine"/> through the L1 scoring service instead of reading a
/// point column — that is what structurally closes FAN-97.
/// </summary>
public class PlayerProjectionDocument
{
    public string Id { get; set; } = string.Empty;
    public string PlayerId { get; set; } = string.Empty;       // gsis_id
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string NflTeam { get; set; } = string.Empty;
    public string OpponentTeam { get; set; } = string.Empty;
    public int Season { get; set; }
    public int Week { get; set; }

    // ── L0: canonical output ──────────────────────────────────────────────
    /// <summary>
    /// Expected per-game stat line in football units. Null on documents written
    /// before Epic 20 — treat null as "legacy, points-only" and fall back to the
    /// cached columns below.
    /// </summary>
    public ProjectedStatLine? StatLine { get; set; }

    /// <summary>
    /// What this projection was built from — see <see cref="Enums.ProjectionBasis"/>.
    /// Stored as a string for query readability, matching the GameScript convention.
    /// "PriorSeasonCarryover" means the number is last season's, shown for this one.
    /// Defaults to "Unknown" so pre-Epic-20 documents, which have no such field,
    /// deserialize as unlabelled rather than falsely claiming to be current.
    /// </summary>
    public string Basis { get; set; } = "Unknown";

    /// <summary>The season whose game logs actually produced this projection.</summary>
    public int BasisSeason { get; set; }

    // ── L1: cached point values (derived, not canonical) ──────────────────
    public decimal ProjectedPoints { get; set; }               // standard (0 PPR)
    public decimal ProjectedPointsPpr { get; set; }            // full PPR
    public decimal ProjectedPointsHalfPpr { get; set; }        // half PPR

    // Model inputs (stored for transparency / tuning)
    public decimal WeightedAvgPoints { get; set; }
    public decimal MatchupAdjustmentFactor { get; set; }
    public decimal SnapPctInput { get; set; }
    public decimal TargetShareInput { get; set; }

    /// <summary>
    /// Volume multiplier applied from the trailing usage windows (3wk/5wk vs season).
    /// &gt;1 = role trending up. Stored so a surprising ranking can be explained.
    /// </summary>
    public decimal UsageTrendMultiplier { get; set; } = 1.0m;

    /// <summary>Share of the basis window in which the player was actually active.</summary>
    public decimal AvailabilityRate { get; set; } = 1.0m;

    // Regression metadata
    public int GameSampleSize { get; set; }
    public decimal RSquared { get; set; }
    public string ScoringFormat { get; set; } = "HalfPpr";
    public DateTime CalculatedAt { get; set; }

    // Game script context — populated when Vegas spread data is available
    // Defaults to Competitive (neutral) when spread is unknown
    public string GameScript { get; set; } = "Unknown";
    public decimal RbVolumeMultiplier { get; set; } = 1.0m;
    public decimal WrTeVolumeMultiplier { get; set; } = 1.0m;
    public decimal SpreadInput { get; set; } = 0m;
}

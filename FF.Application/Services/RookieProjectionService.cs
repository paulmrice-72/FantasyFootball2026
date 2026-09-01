// FF.Application/Services/RookieProjectionService.cs
using FF.Domain.ValueObjects;

namespace FF.Application.Services;

/// <summary>
/// The no-history branch of L0 (Epic 20 / FAN-116).
///
/// A rookie has no NFL game logs, so <see cref="StatLineProjectionService"/> has
/// nothing to regress. This builds a stat line from what IS known about him, in
/// the same football units, so everything downstream — L1 scoring, Monte Carlo,
/// replacement level, roster grades — treats him identically to a veteran.
///
/// Signal hierarchy, and the reasoning behind it:
///
/// 1. **Depth chart position drives volume.** Where a coaching staff actually
///    lines a player up in September is revealed team evaluation — the same class
///    of signal as draft capital, and unlike draft capital it is current and
///    present in the data. It is the only input here that reflects a decision
///    somebody made with real consequences.
///
/// 2. **Combine athleticism nudges efficiency, not volume.** Measurables say
///    something about yards per touch and nothing about whether a coach trusts
///    you. Bounded to +/-10%.
///
/// 3. **FantasyPros rank can move the effective depth tier by at most one, and
///    only on strong disagreement.** This is the deliberate compromise. A pure
///    depth-chart prior systematically under-projects rookies, because September
///    depth charts under-list them as a matter of routine and they climb. Letting
///    consensus say "this player will play more than his listed slot suggests" is
///    a bounded, checkable claim. Letting it set the projection outright would
///    make the product a FantasyPros mirror, which is the thing E20 exists to
///    avoid. So it adjusts opportunity; it never sets it, and it can never move a
///    player more than one tier.
///
/// Everything here is a prior, not a projection in the regression sense. Rookie
/// documents are stamped <c>ProjectionBasis.RookieProjection</c> so consumers can
/// widen their uncertainty — <see cref="MonteCarloSimulationService"/> does.
///
/// Pure and static. No repositories, no I/O.
/// </summary>
public static class RookieProjectionService
{
    public static RookieProjectionResult Project(RookieProjectionInput input)
    {
        var position = (input.Position ?? string.Empty).ToUpperInvariant();

        if (!Volume.ContainsKey(position))
            return RookieProjectionResult.NotSupported(input.SleeperPlayerId, position);

        // ── Effective depth tier ──────────────────────────────────────────
        var listedTier = NormaliseTier(input.DepthTeam);
        var effectiveTier = ApplyConsensusAdjustment(
            listedTier, input.FantasyProsPositionRank, out var consensusMovedTier);

        // A player with neither a depth chart entry nor a consensus rank is not
        // projectable — we would be inventing a role from nothing.
        if (input.DepthTeam is null && input.FantasyProsPositionRank is null)
            return RookieProjectionResult.NoSignal(input.SleeperPlayerId, position);

        var vol = Volume[position][Math.Clamp(effectiveTier, 1, MaxTier) - 1];
        var eff = Efficiency[position];

        // ── Athleticism → efficiency only ─────────────────────────────────
        // AthleticismScore is a 0-100 composite; 50 is average.
        var athletic = input.AthleticismScore is null
            ? 1.0m
            : Clamp(1m + ((decimal)input.AthleticismScore.Value - 50m) / 50m * AthleticismSwing,
                    1m - AthleticismSwing, 1m + AthleticismSwing);

        // ── Rookie discount on volume ─────────────────────────────────────
        // Even at the same listed slot, rookies see less of the field than the
        // veteran the baseline was measured on.
        var discount = RookieVolumeDiscount[position];

        var attempts = vol.PassingAttempts * discount;
        var carries = vol.Carries * discount;
        var targets = vol.Targets * discount;

        var statLine = new ProjectedStatLine
        {
            PassingAttempts = attempts,
            Completions = attempts * eff.CompletionPct,
            PassingYards = attempts * eff.YardsPerAttempt * athletic,
            PassingTds = attempts * eff.PassTdPerAttempt * athletic,
            // Interceptions are not athleticism-scaled — a faster rookie does not
            // throw fewer picks, and rookie QBs throw more of them regardless.
            Interceptions = attempts * eff.IntPerAttempt,

            Carries = carries,
            RushingYards = carries * eff.YardsPerCarry * athletic,
            RushingTds = carries * eff.RushTdPerCarry * athletic,

            Targets = targets,
            Receptions = targets * eff.CatchRate,
            ReceivingYards = targets * eff.YardsPerTarget * athletic,
            ReceivingTds = targets * eff.RecTdPerTarget * athletic,

            FumblesLost = (attempts + carries + targets) * FumbleLostPerOpportunity,
            TwoPointConversions = 0m,
            SpecialTeamsTds = 0m,

            AvailabilityRate = RookieAvailability
        };

        return new RookieProjectionResult
        {
            SleeperPlayerId = input.SleeperPlayerId,
            Position = position,
            StatLine = statLine.Rounded(),
            ListedDepthTier = listedTier,
            EffectiveDepthTier = effectiveTier,
            ConsensusMovedTier = consensusMovedTier,
            AthleticismFactor = Math.Round(athletic, 4),
            HasDepthChart = input.DepthTeam is not null,
            HasConsensusRank = input.FantasyProsPositionRank is not null,
            HasCombine = input.AthleticismScore is not null
        };
    }

    // ── Tunables ─────────────────────────────────────────────────────────
    private const int MaxTier = 4;
    private const decimal AthleticismSwing = 0.10m;          // +/-10% on efficiency
    private const decimal RookieAvailability = 0.88m;
    private const decimal FumbleLostPerOpportunity = 0.004m;

    // Consensus may move a player at most ONE tier, and only when it disagrees
    // strongly with the depth chart. These thresholds are positional ranks within
    // the rookie class.
    private const int ConsensusPromoteRank = 5;              // top-5 rookie at his position
    private const int ConsensusDemoteRank = 25;

    /// <summary>
    /// Depth chart tier is the source of truth for opportunity. Consensus gets one
    /// bounded vote, in one direction, one tier at a time.
    /// </summary>
    private static int ApplyConsensusAdjustment(int listedTier, int? fpPositionRank, out bool moved)
    {
        moved = false;
        if (fpPositionRank is null) return listedTier;

        var rank = fpPositionRank.Value;

        // Consensus says he's a difference-maker but he's listed as depth.
        if (rank > 0 && rank <= ConsensusPromoteRank && listedTier >= 3)
        {
            moved = true;
            return listedTier - 1;
        }

        // Listed as a starter but nobody rates him.
        if (rank >= ConsensusDemoteRank && listedTier == 1)
        {
            moved = true;
            return 2;
        }

        return listedTier;
    }

    /// <summary>
    /// No depth chart entry means we did not find him on a depth chart, not that he
    /// is a starter. Default to fringe (tier 3) and let consensus promote him.
    /// </summary>
    private static int NormaliseTier(int? depthTeam)
        => depthTeam is null or <= 0 ? 3 : Math.Min(depthTeam.Value, MaxTier);

    private static decimal Clamp(decimal v, decimal lo, decimal hi)
        => v < lo ? lo : v > hi ? hi : v;

    // ── Baselines ────────────────────────────────────────────────────────
    // Per-game opportunity by depth tier, indexed [tier-1]. These describe a
    // typical player at that slot; the rookie discount is applied separately so
    // the two assumptions stay legible and independently tunable.

    private sealed record VolumeBaseline(decimal PassingAttempts, decimal Carries, decimal Targets);

    private static readonly Dictionary<string, VolumeBaseline[]> Volume = new()
    {
        ["QB"] =
        [
            new(31.0m, 4.5m, 0m),   // starter
            new(3.5m,  0.6m, 0m),   // backup
            new(0.5m,  0.1m, 0m),
            new(0m,    0m,   0m)
        ],
        ["RB"] =
        [
            new(0m, 13.5m, 3.4m),
            new(0m,  6.5m, 2.0m),
            new(0m,  2.5m, 0.9m),
            new(0m,  0.8m, 0.3m)
        ],
        ["WR"] =
        [
            new(0m, 0.2m, 8.0m),
            new(0m, 0.1m, 5.5m),
            new(0m, 0m,   3.2m),
            new(0m, 0m,   1.3m)
        ],
        ["TE"] =
        [
            new(0m, 0m, 5.2m),
            new(0m, 0m, 2.6m),
            new(0m, 0m, 1.1m),
            new(0m, 0m, 0.4m)
        ]
    };

    // Rookies underperform the slot baseline. TEs worst of all — the position has
    // the steepest learning curve in fantasy football.
    private static readonly Dictionary<string, decimal> RookieVolumeDiscount = new()
    {
        ["QB"] = 0.95m,
        ["RB"] = 0.90m,
        ["WR"] = 0.80m,
        ["TE"] = 0.70m
    };

    private sealed record EfficiencyBaseline(
        decimal CompletionPct,
        decimal YardsPerAttempt,
        decimal PassTdPerAttempt,
        decimal IntPerAttempt,
        decimal YardsPerCarry,
        decimal RushTdPerCarry,
        decimal CatchRate,
        decimal YardsPerTarget,
        decimal RecTdPerTarget);

    // Rookie efficiency, set slightly below the veteran positional baselines used
    // in StatLineProjectionService. Same shape so the two are comparable.
    private static readonly Dictionary<string, EfficiencyBaseline> Efficiency = new()
    {
        ["QB"] = new(0.610m, 6.60m, 0.040m, 0.032m, 4.80m, 0.030m, 0.60m, 6.00m, 0.020m),
        ["RB"] = new(0.500m, 5.00m, 0.030m, 0.030m, 4.10m, 0.026m, 0.740m, 5.80m, 0.018m),
        ["WR"] = new(0.500m, 6.00m, 0.030m, 0.030m, 6.00m, 0.028m, 0.600m, 7.60m, 0.042m),
        ["TE"] = new(0.500m, 6.00m, 0.030m, 0.030m, 3.50m, 0.020m, 0.650m, 6.60m, 0.045m)
    };
}

public class RookieProjectionInput
{
    public string PlayerId { get; set; } = string.Empty;          // gsis, often empty for rookies
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string NflTeam { get; set; } = string.Empty;
    public int Season { get; set; }

    /// <summary>1 = starter. Null when the player was not found on a depth chart.</summary>
    public int? DepthTeam { get; set; }

    /// <summary>FantasyPros rank within his position for the rookie class.</summary>
    public int? FantasyProsPositionRank { get; set; }

    /// <summary>0-100 combine composite; 50 is average. Null when he did not test.</summary>
    public double? AthleticismScore { get; set; }
}

public class RookieProjectionResult
{
    public string SleeperPlayerId { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public ProjectedStatLine StatLine { get; set; } = ProjectedStatLine.Empty;

    public int ListedDepthTier { get; set; }
    public int EffectiveDepthTier { get; set; }
    public bool ConsensusMovedTier { get; set; }
    public decimal AthleticismFactor { get; set; } = 1m;

    public bool HasDepthChart { get; set; }
    public bool HasConsensusRank { get; set; }
    public bool HasCombine { get; set; }

    /// <summary>True when no projection could be produced. Reason is in <see cref="SkipReason"/>.</summary>
    public bool IsSkipped { get; set; }
    public string? SkipReason { get; set; }

    public static RookieProjectionResult NotSupported(string sleeperId, string position) => new()
    {
        SleeperPlayerId = sleeperId,
        Position = position,
        IsSkipped = true,
        SkipReason = $"Position {position} is not projected"
    };

    public static RookieProjectionResult NoSignal(string sleeperId, string position) => new()
    {
        SleeperPlayerId = sleeperId,
        Position = position,
        IsSkipped = true,
        SkipReason = "No depth chart entry and no consensus rank — nothing to project a role from"
    };
}

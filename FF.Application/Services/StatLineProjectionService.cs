// FF.Application/Services/StatLineProjectionService.cs
using FF.Domain.Documents;
using FF.Domain.Enums;
using FF.Domain.ValueObjects;

namespace FF.Application.Services;

/// <summary>
/// L0 of the Unified Projection Engine (Epic 20 / FAN-116).
///
/// Projects an expected per-game STAT LINE — football units, no fantasy points,
/// no league format anywhere in this file. Format-aware work happens at L1
/// (<see cref="FantasyScoringService"/>) and above.
///
/// The model is deliberately volume x efficiency rather than "average the points":
///
///   1. VOLUME (role)      — recency-weighted attempts / carries / targets per game,
///                           then nudged by the trailing usage windows so a player
///                           whose role is trending up isn't held down by a season
///                           average, and vice versa. This is the direct fix for
///                           season averages flattening everyone.
///   2. EFFICIENCY (skill) — yards per opportunity and catch rate computed over the
///                           whole sample, not per game, so a two-target game does
///                           not carry the same weight as a twelve-target game.
///   3. TD RATES           — shrunk toward a positional baseline (empirical-Bayes
///                           style). Touchdowns are the noisiest thing in football
///                           and a per-game average lets three fluke scores outrank
///                           a genuinely better player. Shrinkage is proportional to
///                           sample size, so volume earns you your own rate.
///   4. MATCHUP            — applied to efficiency, not volume: a tough defense
///                           lowers your yards per target, it does not take you off
///                           the field. (The old model applied it to total points,
///                           which implicitly reduced role.)
///
/// Pure and static — no repositories, no I/O, no clock. Everything it needs arrives
/// on <see cref="StatLineProjectionInput"/>, which keeps it unit-testable and keeps
/// FF.Application free of any infrastructure dependency.
/// </summary>
public static class StatLineProjectionService
{
    public static StatLineProjectionResult Project(StatLineProjectionInput input)
    {
        var position = (input.Position ?? string.Empty).ToUpperInvariant();

        var played = input.GameLogs
            .Where(g => DidPlay(g, position))
            .OrderByDescending(g => g.Season * 100 + g.Week)
            .ToList();

        if (played.Count < input.Weights.MinGamesRequired)
            return StatLineProjectionResult.Insufficient(
                input.PlayerId, input.Basis, input.BasisSeason);

        // ── Recency weighting ─────────────────────────────────────────────
        var weights = BuildRecencyWeights(played.Count, input.Weights.RecentGameWeight);
        var weightSum = weights.Sum();

        decimal WeightedPerGame(Func<PlayerGameLogDocument, decimal> selector)
        {
            if (weightSum <= 0) return 0m;
            var acc = 0m;
            for (var i = 0; i < played.Count; i++)
                acc += selector(played[i]) * (decimal)weights[i];
            return acc / (decimal)weightSum;
        }

        // ── 1. Volume, per game ───────────────────────────────────────────
        var attemptsPerGame = WeightedPerGame(g => g.Attempts);
        var carriesPerGame = WeightedPerGame(g => g.Carries);
        var targetsPerGame = WeightedPerGame(g => g.Targets);

        // ── 2. Efficiency, over the whole sample ──────────────────────────
        var totalAttempts = played.Sum(g => (decimal)g.Attempts);
        var totalCarries = played.Sum(g => (decimal)g.Carries);
        var totalTargets = played.Sum(g => (decimal)g.Targets);

        var b = PositionBaselines.For(position);

        var completionPct = Rate(played.Sum(g => (decimal)g.Completions), totalAttempts, b.CompletionPct);
        var yardsPerAttempt = Rate(played.Sum(g => g.PassingYards), totalAttempts, b.YardsPerAttempt);
        var yardsPerCarry = Rate(played.Sum(g => g.RushingYards), totalCarries, b.YardsPerCarry);
        var catchRate = Rate(played.Sum(g => (decimal)g.Receptions), totalTargets, b.CatchRate);
        var yardsPerTarget = Rate(played.Sum(g => g.ReceivingYards), totalTargets, b.YardsPerTarget);

        // ── 3. Touchdown / turnover rates, shrunk toward the baseline ─────
        var passTdRate = Shrink(played.Sum(g => (decimal)g.PassingTds), totalAttempts, b.PassTdPerAttempt, PassPriorStrength);
        var intRate = Shrink(played.Sum(g => (decimal)g.Interceptions), totalAttempts, b.IntPerAttempt, PassPriorStrength);
        var rushTdRate = Shrink(played.Sum(g => (decimal)g.RushingTds), totalCarries, b.RushTdPerCarry, RushPriorStrength);
        var recTdRate = Shrink(played.Sum(g => (decimal)g.ReceivingTds), totalTargets, b.RecTdPerTarget, RecPriorStrength);

        // ── 4. Adjustments ────────────────────────────────────────────────
        // Matchup scales efficiency only. DifficultyScore is 0-100, 50 = neutral.
        var matchupFactor = 1m + ((50m - Clamp(input.MatchupDifficultyScore, 0m, 100m)) / 50m) * MatchupSwing;

        var usage = UsageTrend(position, input.Usage, played.Count);

        // Game script and age scale volume — they change how much a player is used,
        // not how well he plays when used.
        // Game script only moves the position it was measured on: the RB multiplier
        // applies to a running back's carries, the WR/TE multiplier to a receiver's
        // targets. A WR's handful of jet-sweep carries does not get a blowout boost.
        var gsRush = position == "RB" ? input.GameScriptRbMultiplier : 1.0m;
        var gsRecv = position is "WR" or "TE" ? input.GameScriptWrTeMultiplier : 1.0m;

        var passVolumeFactor = usage.PassMultiplier * input.AgeAdjustmentFactor;
        var rushVolumeFactor = usage.RushMultiplier * gsRush * input.AgeAdjustmentFactor;
        var recvVolumeFactor = usage.RecvMultiplier * gsRecv * input.AgeAdjustmentFactor;

        var projAttempts = attemptsPerGame * passVolumeFactor;
        var projCarries = carriesPerGame * rushVolumeFactor;
        var projTargets = targetsPerGame * recvVolumeFactor;

        // ── Assemble ──────────────────────────────────────────────────────
        var statLine = new ProjectedStatLine
        {
            PassingAttempts = projAttempts,
            Completions = projAttempts * completionPct,
            PassingYards = projAttempts * yardsPerAttempt * matchupFactor,
            PassingTds = projAttempts * passTdRate * matchupFactor,
            // Interceptions are NOT matchup-scaled — a tougher defense would
            // increase them, and applying the same factor would wrongly reduce them.
            Interceptions = projAttempts * intRate,

            Carries = projCarries,
            RushingYards = projCarries * yardsPerCarry * matchupFactor,
            RushingTds = projCarries * rushTdRate * matchupFactor,

            Targets = projTargets,
            Receptions = projTargets * catchRate,
            ReceivingYards = projTargets * yardsPerTarget * matchupFactor,
            ReceivingTds = projTargets * recTdRate * matchupFactor,

            FumblesLost = WeightedPerGame(g =>
                g.RushingFumblesLost + g.ReceivingFumblesLost + g.SackFumblesLost),
            TwoPointConversions = WeightedPerGame(g =>
                g.Passing2PtConversions + g.Rushing2PtConversions + g.Receiving2PtConversions),
            SpecialTeamsTds = WeightedPerGame(g => g.SpecialTeamsTds),

            AvailabilityRate = Availability(input.GameLogs, played.Count)
        };

        return new StatLineProjectionResult
        {
            PlayerId = input.PlayerId,
            Position = position,
            StatLine = statLine.Rounded(),
            Basis = input.Basis,
            BasisSeason = input.BasisSeason,
            GameSampleSize = played.Count,
            MatchupAdjustmentFactor = Math.Round(matchupFactor, 4),
            UsageTrendMultiplier = Math.Round(usage.Headline, 4),
            AvailabilityRate = statLine.AvailabilityRate,
            SnapPctInput = input.Usage?.SnapPct3Wk ?? 0m,
            TargetShareInput = input.Usage?.TargetShare3Wk ?? 0m
        };
    }

    // ── Tunables ─────────────────────────────────────────────────────────
    // Prior strength = "how many opportunities before you own your own rate".
    // At exactly this many opportunities the projected rate sits halfway between
    // the player's observed rate and the positional baseline.
    private const decimal PassPriorStrength = 120m;   // ~3.5 games of attempts
    private const decimal RushPriorStrength = 55m;    // ~4 games of a starter's carries
    private const decimal RecPriorStrength = 45m;     // ~5 games of a WR1's targets

    private const decimal MatchupSwing = 0.20m;       // +/-20% on efficiency

    // Usage trend. Calibrated 2026-09-01 against the first real run (584 WR/TE rows):
    // the original 0.75-1.35 band at full strength pinned 21% of the population at a
    // clamp and moved players up to 38 ranks on trailing-window noise alone — Parker
    // Washington to WR9, Olszewski to WR22, McConkey down to WR76. A late-season
    // target spike is usually somebody else's injury, not a durable role change.
    // Damping to half strength, scaled by sample size, cut the worst distortion to
    // 18 ranks and the 95th percentile from 32 to 15.
    private const decimal UsageTrendStrength = 0.50m;      // max share of the raw deviation
    private const decimal UsageTrendFloor = 0.90m;
    private const decimal UsageTrendCeiling = 1.15m;
    private const decimal QbUsageTrendFloor = 0.95m;       // QB volume is far less elastic
    private const decimal QbUsageTrendCeiling = 1.08m;
    private const int UsageTrendMinGames = 3;              // below this, no trend at all
    private const int UsageTrendFullGames = 12;            // at/above this, full strength

    // ── Helpers ──────────────────────────────────────────────────────────

    private static bool DidPlay(PlayerGameLogDocument g, string position) =>
        position switch
        {
            "QB" => g.Completions > 0 || g.PassingYards > 0 || g.Carries > 0,
            "RB" => g.Carries > 0 || g.Targets > 0 || g.OffenseSnaps > 0,
            "WR" => g.Targets > 0 || g.ReceivingYards > 0 || g.OffenseSnaps > 0,
            "TE" => g.Targets > 0 || g.ReceivingYards > 0 || g.OffenseSnaps > 0,
            _ => g.Targets > 0 || g.Carries > 0 || g.OffenseSnaps > 0
        };

    private static double[] BuildRecencyWeights(int count, decimal recentBias)
    {
        var weights = new double[count];
        if (count == 1) { weights[0] = 1.0; return weights; }

        // Exponential decay: index 0 is the most recent game (weight 1.0);
        // the oldest game in the sample gets (1 - recentBias).
        var decayBase = (double)(1m - recentBias);
        if (decayBase <= 0) decayBase = 0.01;

        for (var i = 0; i < count; i++)
            weights[i] = Math.Pow(decayBase, i / (double)(count - 1));

        return weights;
    }

    /// <summary>Observed rate, or the baseline when there is no denominator.</summary>
    private static decimal Rate(decimal numerator, decimal denominator, decimal fallback) =>
        denominator <= 0m ? fallback : numerator / denominator;

    /// <summary>
    /// Empirical-Bayes style shrinkage. With few opportunities the projected rate
    /// stays near the positional baseline; with many it converges on the player's own.
    /// </summary>
    private static decimal Shrink(
        decimal observedEvents,
        decimal observedOpportunities,
        decimal baselineRate,
        decimal priorStrength)
    {
        if (observedOpportunities <= 0m) return baselineRate;
        return (observedEvents + baselineRate * priorStrength)
             / (observedOpportunities + priorStrength);
    }

    /// <summary>
    /// Share of the basis window in which the player actually took the field.
    /// Denominator is the span of weeks covered by the supplied logs, so a player
    /// whose logs only start in Week 6 isn't punished for the weeks before he existed
    /// in the dataset — but one who has a Week 1 log and then missed six weeks is.
    /// </summary>
    private static decimal Availability(IReadOnlyList<PlayerGameLogDocument> allLogs, int gamesPlayed)
    {
        if (allLogs.Count == 0) return 1m;

        var minWeek = allLogs.Min(g => g.Week);
        var maxWeek = allLogs.Max(g => g.Week);
        var span = maxWeek - minWeek + 1;

        if (span <= 0) return 1m;

        return Clamp((decimal)gamesPlayed / span, 0.1m, 1m);
    }

    /// <summary>
    /// Turns the trailing usage windows into volume multipliers.
    ///
    /// Ratio of the recency-blended share to the season share: above 1 means the
    /// player's recent role is bigger than his season line suggests. Two things keep
    /// that from running away, both learned from the first real run:
    ///
    /// * the raw ratio is damped to at most <see cref="UsageTrendStrength"/> of its
    ///   deviation from 1.0, because a three-week share is a small, noisy sample and
    ///   a late-season spike is usually a team-mate's injury rather than a promotion;
    /// * the damping is scaled by how many games actually back the trend, on the same
    ///   principle as the touchdown shrinkage above — a player with four games does
    ///   not get to move as far as one with fourteen.
    /// </summary>
    private static UsageMultipliers UsageTrend(
        string position, PlayerUsageMetricsDocument? u, int gameCount)
    {
        if (u is null) return UsageMultipliers.Neutral;

        var (floor, ceiling) = position == "QB"
            ? (QbUsageTrendFloor, QbUsageTrendCeiling)
            : (UsageTrendFloor, UsageTrendCeiling);

        // 0 at the minimum sample, rising to full strength at UsageTrendFullGames.
        var span = (decimal)(UsageTrendFullGames - UsageTrendMinGames);
        var sampleWeight = Clamp((gameCount - UsageTrendMinGames) / span, 0m, 1m);
        var strength = sampleWeight * UsageTrendStrength;

        decimal Trend(decimal w3, decimal w5, decimal season)
        {
            if (season <= 0m) return 1m;
            var blended = 0.50m * w3 + 0.30m * w5 + 0.20m * season;
            var raw = blended / season;
            return Clamp(1m + (raw - 1m) * strength, floor, ceiling);
        }

        var recv = Trend(u.TargetShare3Wk, u.TargetShare5Wk, u.TargetShareSeason);
        var rush = Trend(u.CarryShare3Wk, u.CarryShare5Wk, u.CarryShareSeason);
        var pass = Trend(u.SnapPct3Wk, u.SnapPct5Wk, u.SnapPctSeason);

        var headline = position switch
        {
            "QB" => pass,
            "RB" => rush,
            _ => recv
        };

        return new UsageMultipliers(pass, rush, recv, headline);
    }

    private static decimal Clamp(decimal value, decimal min, decimal max) =>
        value < min ? min : value > max ? max : value;

    private readonly record struct UsageMultipliers(
        decimal PassMultiplier,
        decimal RushMultiplier,
        decimal RecvMultiplier,
        decimal Headline)
    {
        public static UsageMultipliers Neutral => new(1m, 1m, 1m, 1m);
    }

    /// <summary>
    /// Positional priors. These are the ONLY constants in the model, and every one of
    /// them is a real football rate that can be re-derived from the game log corpus —
    /// deliberately not the hand-tuned fudge factors the calibration-debt review
    /// flagged. PROJ-002 should recompute them from nflverse rather than hardcode.
    /// </summary>
    private sealed record PositionBaselines(
        decimal CompletionPct,
        decimal YardsPerAttempt,
        decimal PassTdPerAttempt,
        decimal IntPerAttempt,
        decimal YardsPerCarry,
        decimal RushTdPerCarry,
        decimal CatchRate,
        decimal YardsPerTarget,
        decimal RecTdPerTarget)
    {
        public static PositionBaselines For(string position) => position switch
        {
            "QB" => new(0.645m, 7.10m, 0.046m, 0.026m, 5.20m, 0.038m, 0.60m, 6.00m, 0.020m),
            "RB" => new(0.500m, 5.00m, 0.030m, 0.030m, 4.30m, 0.030m, 0.75m, 6.20m, 0.022m),
            "WR" => new(0.500m, 6.00m, 0.030m, 0.030m, 6.50m, 0.030m, 0.630m, 8.40m, 0.048m),
            "TE" => new(0.500m, 6.00m, 0.030m, 0.030m, 3.50m, 0.020m, 0.680m, 7.60m, 0.060m),
            _ => new(0.600m, 6.50m, 0.035m, 0.028m, 4.20m, 0.028m, 0.650m, 7.50m, 0.040m)
        };
    }
}

/// <summary>Everything L0 needs. Assembled by ProjectionInputBuilder.</summary>
public class StatLineProjectionInput
{
    public string PlayerId { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;

    /// <summary>
    /// Game logs from the basis season. Pass ALL of them, including games the player
    /// missed if rows exist — availability is derived from this list before filtering.
    /// </summary>
    public IReadOnlyList<PlayerGameLogDocument> GameLogs { get; set; } = [];

    public ProjectionBasis Basis { get; set; } = ProjectionBasis.CurrentSeason;
    public int BasisSeason { get; set; }

    public PlayerUsageMetricsDocument? Usage { get; set; }

    /// <summary>0-100, 50 = neutral. Defaults to neutral when no ranking exists.</summary>
    public decimal MatchupDifficultyScore { get; set; } = 50m;

    /// <summary>
    /// Volume multipliers from GameScriptClassifier. 1.0 when no Vegas line is posted.
    /// </summary>
    public decimal GameScriptRbMultiplier { get; set; } = 1.0m;
    public decimal GameScriptWrTeMultiplier { get; set; } = 1.0m;

    /// <summary>
    /// Aging adjustment for the prior-season carryover path. Left at 1.0 by PROJ-001;
    /// wired to the aging curves in PROJ-004 (FAN-119), which owns the time horizon.
    /// </summary>
    public decimal AgeAdjustmentFactor { get; set; } = 1.0m;

    public ProjectionWeightProfile Weights { get; set; } = ProjectionWeightProfile.Default;
}

public class StatLineProjectionResult
{
    public string PlayerId { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public ProjectedStatLine StatLine { get; set; } = ProjectedStatLine.Empty;

    public ProjectionBasis Basis { get; set; } = ProjectionBasis.None;
    public int BasisSeason { get; set; }

    public int GameSampleSize { get; set; }
    public decimal MatchupAdjustmentFactor { get; set; } = 1m;
    public decimal UsageTrendMultiplier { get; set; } = 1m;
    public decimal AvailabilityRate { get; set; } = 1m;
    public decimal SnapPctInput { get; set; }
    public decimal TargetShareInput { get; set; }

    public bool IsInsufficient { get; set; }

    public static StatLineProjectionResult Insufficient(
        string playerId, ProjectionBasis basis, int basisSeason) => new()
        {
            PlayerId = playerId,
            Basis = basis,
            BasisSeason = basisSeason,
            IsInsufficient = true
        };
}

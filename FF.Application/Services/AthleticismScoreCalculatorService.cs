// FF.Application/Services/AthleticismScoreCalculator.cs
using FF.Domain.Documents;

namespace FF.Application.Services;

/// <summary>
/// Computes a 0-100 athleticism composite from NFL combine measurements.
///
/// Design: each metric is normalized against historical position benchmarks
/// (elite = 100, floor = 0, linear interpolation). Only metrics with actual
/// data contribute. Active metric weights are normalized to fill 100%.
///
/// Position-specific metric weights (must conceptually sum to 1.0 per position):
///   RB:  forty 35%, vertical 20%, broad 20%, cone 15%, shuttle 10%
///   WR:  forty 35%, vertical 25%, broad 20%, cone 10%, shuttle 10%
///   TE:  forty 30%, vertical 25%, broad 20%, cone 15%, shuttle 10%
///   QB:  forty 20%, vertical 15%, broad 15%, cone 25%, shuttle 25%
///   DEF/K/default: forty 30%, vertical 20%, broad 20%, cone 15%, shuttle 15%
///
/// Speed Score (RBs only): (weight × 200) / (forty²) — Bill Barnwell metric.
/// Historically elite RBs score 100+; normalized against 115 elite / 85 floor.
/// </summary>
public static class AthleticismScoreCalculator
{
    public static double Calculate(CombineResultDocument combine)
    {
        var pos = combine.Position.ToUpperInvariant();
        var weights = GetWeights(pos);

        var activeMetrics = new List<(double Score, double Weight)>();

        if (combine.FortyYard.HasValue)
            activeMetrics.Add((ScoreForty(combine.FortyYard.Value, pos), weights.Forty));

        if (combine.Vertical.HasValue)
            activeMetrics.Add((ScoreVertical(combine.Vertical.Value), weights.Vertical));

        if (combine.BroadJump.HasValue)
            activeMetrics.Add((ScoreBroadJump(combine.BroadJump.Value), weights.Broad));

        if (combine.ConeDrill.HasValue)
            activeMetrics.Add((ScoreCone(combine.ConeDrill.Value), weights.Cone));

        if (combine.Shuttle.HasValue)
            activeMetrics.Add((ScoreShuttle(combine.Shuttle.Value), weights.Shuttle));

        if (!activeMetrics.Any()) return 0;

        // Normalize weights across only active metrics
        var totalWeight = activeMetrics.Sum(m => m.Weight);
        var composite = activeMetrics.Sum(m => m.Score * (m.Weight / totalWeight));

        return Math.Round(Math.Clamp(composite, 0, 100), 1);
    }

    public static double? ComputeSpeedScore(double? weightLbs, double? fortyYard)
    {
        if (weightLbs is null || fortyYard is null || fortyYard <= 0) return null;
        return Math.Round((weightLbs.Value * 200.0) / (fortyYard.Value * fortyYard.Value), 1);
    }

    // ── Position weight profiles ──────────────────────────────────────────
    private record MetricWeights(double Forty, double Vertical, double Broad, double Cone, double Shuttle);

    private static MetricWeights GetWeights(string pos) => pos switch
    {
        "RB" => new(0.35, 0.20, 0.20, 0.15, 0.10),
        "WR" => new(0.35, 0.25, 0.20, 0.10, 0.10),
        "TE" => new(0.30, 0.25, 0.20, 0.15, 0.10),
        "QB" => new(0.20, 0.15, 0.15, 0.25, 0.25),
        _ => new(0.30, 0.20, 0.20, 0.15, 0.15)
    };

    // ── Metric scorers — lower time = better for timed events ─────────────

    /// <summary>
    /// 40-yard dash. Elite/floor benchmarks vary by position.
    /// RB/WR elite ≤ 4.33, floor ≥ 4.70
    /// TE elite ≤ 4.45, floor ≥ 4.85
    /// QB elite ≤ 4.50, floor ≥ 5.00
    /// </summary>
    public static double ScoreForty(double forty, string pos)
    {
        var (elite, floor) = pos switch
        {
            "RB" or "WR" => (4.33, 4.70),
            "TE" => (4.45, 4.85),
            "QB" => (4.50, 5.00),
            _ => (4.40, 4.80)
        };
        // Lower is better — invert the scale
        return Normalize(floor, elite, forty);
    }

    /// <summary>
    /// Vertical jump. Elite ≥ 42", floor ≤ 28". Higher is better.
    /// </summary>
    public static double ScoreVertical(double vertical) =>
        Normalize(28.0, 42.0, vertical);

    /// <summary>
    /// Broad jump. Elite ≥ 135", floor ≤ 105". Higher is better.
    /// </summary>
    public static double ScoreBroadJump(double broadJump) =>
        Normalize(105.0, 135.0, broadJump);

    /// <summary>
    /// 3-cone drill. Elite ≤ 6.45s, floor ≥ 7.30s. Lower is better.
    /// </summary>
    public static double ScoreCone(double cone) =>
        Normalize(7.30, 6.45, cone);

    /// <summary>
    /// 20-yard shuttle. Elite ≤ 3.90s, floor ≥ 4.45s. Lower is better.
    /// </summary>
    public static double ScoreShuttle(double shuttle) =>
        Normalize(4.45, 3.90, shuttle);

    /// <summary>
    /// Linear normalization.
    /// When floor < elite (higher=better): score = (value-floor)/(elite-floor)*100
    /// When floor > elite (lower=better):  score = (floor-value)/(floor-elite)*100
    /// </summary>
    private static double Normalize(double floor, double elite, double value)
    {
        if (Math.Abs(elite - floor) < 0.0001) return 50;
        var score = (value - floor) / (elite - floor) * 100.0;
        return Math.Clamp(score, 0, 100);
    }
}
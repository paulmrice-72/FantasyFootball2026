// FF.Application/Services/Usage/UsageMetricsCalculator.cs
namespace FF.Application.Interfaces.Services.Usage;

public static class UsageMetricsCalculator
{
    // Weighted rolling average — most recent week gets highest weight
    // weights = [3, 2, 1] for 3-week, [5, 4, 3, 2, 1] for 5-week
    public static decimal WeightedAverage(IList<decimal> values, int window)
    {
        var slice = values.TakeLast(window).ToList();
        if (slice.Count == 0) return 0m;

        var weights = Enumerable.Range(1, slice.Count).Select(i => (decimal)i).ToList();
        var weightedSum = slice.Zip(weights, (v, w) => v * w).Sum();
        var weightTotal = weights.Sum();

        return Math.Round(weightTotal == 0 ? 0m : weightedSum / weightTotal, 4);
    }

    // WOPR = (TargetShare * 1.5) + (AirYardsShare * 0.7)
    // Standard nflfastR definition
    public static decimal CalculateWopr(decimal targetShare, decimal airYardsShare)
        => (targetShare * 1.5m) + (airYardsShare * 0.7m);

    public static decimal SimpleAverage(IList<decimal> values)
        => values.Any() ? Math.Round(values.Average(), 4) : 0m;
}
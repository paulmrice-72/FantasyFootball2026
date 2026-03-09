// FF.Tests/Unit/Services/UsageMetricsCalculatorTests.cs
using FF.Application.Interfaces.Services.Usage;

namespace FF.Tests.Unit.Services;

public class UsageMetricsCalculatorTests
{
    [Fact]
    public void WeightedAverage_3Week_AppliesCorrectWeights()
    {
        // Arrange — weeks 1,2,3 with values 0.10, 0.20, 0.30
        // Weights: week1=1, week2=2, week3=3
        // Expected: (0.10*1 + 0.20*2 + 0.30*3) / (1+2+3) = 1.40/6 = 0.2333
        var values = new List<decimal> { 0.10m, 0.20m, 0.30m };

        // Act
        var result = UsageMetricsCalculator.WeightedAverage(values, 3);

        // Assert
        Assert.Equal(0.2333m, Math.Round(result, 4));
    }

    [Fact]
    public void WeightedAverage_FewerWeeksThanWindow_UsesAvailableWeeks()
    {
        // Player only has 2 weeks but we request 5-week average
        var values = new List<decimal> { 0.20m, 0.30m };

        var result = UsageMetricsCalculator.WeightedAverage(values, 5);

        // Should not throw — uses 2-week weighted average instead
        Assert.True(result > 0m);
    }

    [Fact]
    public void WeightedAverage_EmptyList_ReturnsZero()
    {
        var result = UsageMetricsCalculator
            .WeightedAverage([], 3);

        Assert.Equal(0m, result);
    }

    [Fact]
    public void CalculateWopr_UsesCorrectFormula()
    {
        // WOPR = (targetShare * 1.5) + (airYardsShare * 0.7)
        // (0.25 * 1.5) + (0.30 * 0.7) = 0.375 + 0.210 = 0.585
        var result = UsageMetricsCalculator.CalculateWopr(0.25m, 0.30m);

        Assert.Equal(0.585m, result);
    }

    [Fact]
    public void SimpleAverage_ReturnsCorrectMean()
    {
        var values = new List<decimal> { 0.10m, 0.20m, 0.30m };

        var result = UsageMetricsCalculator.SimpleAverage(values);

        Assert.Equal(0.20m, result);
    }
}
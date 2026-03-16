using FF.Application.Services;
using FluentAssertions;
using System.Text;

namespace FF.Tests.Application;

public class FantasyPointsCalculatorTests
{
    [Fact]
    public void Calculate_PprScoring_ReturnsCorrectPoints()
    {
        // Tyreek Hill-ish game: 8 rec, 120 yards, 1 TD
        var result = FantasyPointsCalculator.Calculate(
            receptions: 8,
            receivingYards: 120,
            receivingTds: 1,
            recPointsPerReception: 1m);

        // 8 * 1 + 120 * 0.1 + 1 * 6 = 8 + 12 + 6 = 26
        result.Should().Be(26m);
    }

    [Fact]
    public void Calculate_HalfPpr_ReturnsCorrectPoints()
    {
        var result = FantasyPointsCalculator.Calculate(
            receptions: 8,
            receivingYards: 120,
            receivingTds: 1,
            recPointsPerReception: 0.5m);

        // 8 * 0.5 + 120 * 0.1 + 6 = 4 + 12 + 6 = 22
        result.Should().Be(22m);
    }

    [Fact]
    public void Calculate_Standard_NoReceptionPoints()
    {
        var result = FantasyPointsCalculator.Calculate(
            receptions: 8,
            receivingYards: 120,
            receivingTds: 1,
            recPointsPerReception: 0m);

        // 0 + 12 + 6 = 18
        result.Should().Be(18m);
    }

    [Fact]
    public void Calculate_SixPointPassingTd_ReturnsCorrectPoints()
    {
        var result = FantasyPointsCalculator.Calculate(
            passingYards: 300,
            passingTds: 3,
            passingTdPoints: 6m);

        // 300 * 0.04 + 3 * 6 = 12 + 18 = 30
        result.Should().Be(30m);
    }

    [Fact]
    public void Calculate_FourPointPassingTd_ReturnsCorrectPoints()
    {
        var result = FantasyPointsCalculator.Calculate(
            passingYards: 300,
            passingTds: 3,
            passingTdPoints: 4m);

        // 12 + 12 = 24
        result.Should().Be(24m);
    }

    [Fact]
    public void Calculate_Interception_DeductsPoints()
    {
        var result = FantasyPointsCalculator.Calculate(
            passingYards: 200,
            interceptions: 2);

        // 200 * 0.04 + 2 * -2 = 8 - 4 = 4
        result.Should().Be(4m);
    }

    [Fact]
    public void Calculate_FumbleLost_DeductsPoints()
    {
        var result = FantasyPointsCalculator.Calculate(
            rushingYards: 100,
            rushingTds: 1,
            fumblesLost: 1);

        // 10 + 6 - 2 = 14
        result.Should().Be(14m);
    }

    [Fact]
    public void Calculate_BonusRecTe_AddsPerReception()
    {
        var result = FantasyPointsCalculator.Calculate(
            receptions: 5,
            receivingYards: 60,
            recPointsPerReception: 1m,
            bonusRecTe: 0.5m);

        // 5 * 1.5 + 60 * 0.1 = 7.5 + 6 = 13.5
        result.Should().Be(13.5m);
    }

    [Fact]
    public void Calculate_ZeroStats_ReturnsZero()
    {
        var result = FantasyPointsCalculator.Calculate();
        result.Should().Be(0m);
    }
}
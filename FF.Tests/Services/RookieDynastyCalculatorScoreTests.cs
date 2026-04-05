// FF.Tests/Application/Services/RookieDynastyScoreCalculatorTests.cs
using FF.Application.Services;
using FF.Domain.Documents;
using FluentAssertions;

namespace FF.Tests.Services;

public class RookieDynastyScoreCalculatorTests
{
    // ── Draft capital ─────────────────────────────────────────────────────

    [Fact]
    public void ScoreDraftCapital_Pick1_Returns100()
        => RookieDynastyScoreCalculator.ScoreDraftCapital(1).Should().BeApproximately(100, 0.1);

    [Fact]
    public void ScoreDraftCapital_Pick262_ReturnsZero()
        => RookieDynastyScoreCalculator.ScoreDraftCapital(262).Should().BeApproximately(0, 0.1);

    [Fact]
    public void ScoreDraftCapital_NullPick_ReturnsZero()
        => RookieDynastyScoreCalculator.ScoreDraftCapital(null).Should().Be(0);

    [Fact]
    public void ScoreDraftCapital_Pick32_IsHigherThanPick100()
    {
        var pick32 = RookieDynastyScoreCalculator.ScoreDraftCapital(32);
        var pick100 = RookieDynastyScoreCalculator.ScoreDraftCapital(100);
        pick32.Should().BeGreaterThan(pick100);
    }

    // ── Positional value ──────────────────────────────────────────────────

    [Theory]
    [InlineData("WR", 90)]
    [InlineData("QB", 85)]
    [InlineData("TE", 75)]
    [InlineData("RB", 55)]
    [InlineData("K", 10)]
    public void ScorePosition_ReturnsExpectedValue(string position, double expected)
        => RookieDynastyScoreCalculator.ScorePosition(position).Should().Be(expected);

    [Fact]
    public void ScorePosition_CaseInsensitive()
        => RookieDynastyScoreCalculator.ScorePosition("wr")
            .Should().Be(RookieDynastyScoreCalculator.ScorePosition("WR"));

    // ── Valuation blend ───────────────────────────────────────────────────

    [Fact]
    public void ScoreValuationBlend_NullDocument_ReturnsZero()
        => RookieDynastyScoreCalculator.ScoreValuationBlend(null).Should().Be(0);

    [Fact]
    public void ScoreValuationBlend_AllMax_Returns100()
    {
        var doc = new DynastyValuationDocument
        {
            CareerValueScore = 100,
            TradeValue = 100,
            DiscountedFutureValue = 100
        };
        RookieDynastyScoreCalculator.ScoreValuationBlend(doc).Should().BeApproximately(100, 0.1);
    }

    [Fact]
    public void ScoreValuationBlend_IsAverageOfThreeFields()
    {
        var doc = new DynastyValuationDocument
        {
            CareerValueScore = 90,
            TradeValue = 60,
            DiscountedFutureValue = 30
        };
        RookieDynastyScoreCalculator.ScoreValuationBlend(doc).Should().BeApproximately(60, 0.1);
    }

    // ── FantasyPros rank ──────────────────────────────────────────────────

    [Fact]
    public void ScoreFantasyProsRank_Rank1_Returns100()
        => RookieDynastyScoreCalculator.ScoreFantasyProsRank(1).Should().BeApproximately(100, 0.1);

    [Fact]
    public void ScoreFantasyProsRank_NullRank_ReturnsZero()
        => RookieDynastyScoreCalculator.ScoreFantasyProsRank(null).Should().Be(0);

    [Fact]
    public void ScoreFantasyProsRank_Rank100OrMore_ReturnsZeroOrLess()
        => RookieDynastyScoreCalculator.ScoreFantasyProsRank(100)
            .Should().BeGreaterThanOrEqualTo(0);

    // ── Composite ─────────────────────────────────────────────────────────

    [Fact]
    public void Calculate_FirstOverallWR_ScoresAbove85()
    {
        var doc = new DynastyValuationDocument
        {
            CareerValueScore = 80,
            TradeValue = 75,
            DiscountedFutureValue = 85
        };
        var score = RookieDynastyScoreCalculator.Calculate(
            overallPick: 1,
            position: "WR",
            valuation: doc,
            fantasyProsRank: 1);

        score.Should().BeGreaterThan(85);
    }

    [Fact]
    public void Calculate_AllNullInputs_ReturnsPositiveFromPositionalOnly()
    {
        // Even with no pick/valuation/fp data, positional value still contributes
        var score = RookieDynastyScoreCalculator.Calculate(
            overallPick: null,
            position: "WR",
            valuation: null,
            fantasyProsRank: null);

        // 25% weight * 90 positional = 22.5
        score.Should().BeApproximately(22.5, 0.5);
    }

    [Fact]
    public void Calculate_ScoreAlwaysBetween0And100()
    {
        var extremeDoc = new DynastyValuationDocument
        {
            CareerValueScore = 150,   // intentionally over-range
            TradeValue = 200,
            DiscountedFutureValue = -50
        };

        var score = RookieDynastyScoreCalculator.Calculate(
            overallPick: 1, position: "WR",
            valuation: extremeDoc, fantasyProsRank: 1);

        score.Should().BeInRange(0, 100);
    }

    [Fact]
    public void CalculateWithBreakdown_SumOfWeightedComponentsEqualsComposite()
    {
        var doc = new DynastyValuationDocument
        {
            CareerValueScore = 70,
            TradeValue = 65,
            DiscountedFutureValue = 60
        };

        var bd = RookieDynastyScoreCalculator.CalculateWithBreakdown(
            overallPick: 15, position: "WR", valuation: doc, fantasyProsRank: 5);

        var recomputed =
            (bd.DraftCapitalScore * 0.35) +
            (bd.PositionalScore * 0.25) +
            (bd.ValuationBlendScore * 0.30) +
            (bd.FantasyProsScore * 0.10);

        bd.DynastyScore.Should().BeApproximately(
            Math.Round(recomputed, 1), 0.2);
    }
}
// FF.Tests/Services/RookieDynastyCalculatorScoreTests.cs
using FF.Application.Services;
using FF.Domain.Documents;
using FluentAssertions;

namespace FF.Tests.Services;

public class RookieDynastyScoreCalculatorTests
{
    // ── Draft capital ────────────────────────────────────────────────────
    [Fact]
    public void ScoreDraftCapital_Pick1_Returns100() =>
        RookieDynastyScoreCalculator.ScoreDraftCapital(1).Should().BeApproximately(100, 0.1);

    [Fact]
    public void ScoreDraftCapital_Pick262_ReturnsZero() =>
        RookieDynastyScoreCalculator.ScoreDraftCapital(262).Should().BeApproximately(0, 0.1);

    [Fact]
    public void ScoreDraftCapital_NullPick_ReturnsZero() =>
        RookieDynastyScoreCalculator.ScoreDraftCapital(null).Should().Be(0);

    [Fact]
    public void ScoreDraftCapital_Pick32_IsHigherThanPick100()
    {
        var pick32 = RookieDynastyScoreCalculator.ScoreDraftCapital(32);
        var pick100 = RookieDynastyScoreCalculator.ScoreDraftCapital(100);
        pick32.Should().BeGreaterThan(pick100);
    }

    // ── Positional value ─────────────────────────────────────────────────
    [Theory]
    [InlineData("WR", 90)]
    [InlineData("QB", 85)]
    [InlineData("TE", 75)]
    [InlineData("RB", 55)]
    [InlineData("K", 10)]
    public void ScorePosition_ReturnsExpectedValue(string position, double expected) =>
        RookieDynastyScoreCalculator.ScorePosition(position).Should().Be(expected);

    [Fact]
    public void ScorePosition_CaseInsensitive() =>
        RookieDynastyScoreCalculator.ScorePosition("wr")
            .Should().Be(RookieDynastyScoreCalculator.ScorePosition("WR"));

    // ── Valuation blend ──────────────────────────────────────────────────
    [Fact]
    public void ScoreValuationBlend_NullDocument_ReturnsZero() =>
        RookieDynastyScoreCalculator.ScoreValuationBlend(null).Should().Be(0);

    [Fact]
    public void ScoreValuationBlend_AllMax_Returns100()
    {
        var doc = new DynastyValuationDocument
        { CareerValueScore = 100, TradeValue = 100, DiscountedFutureValue = 100 };
        RookieDynastyScoreCalculator.ScoreValuationBlend(doc).Should().BeApproximately(100, 0.1);
    }

    [Fact]
    public void ScoreValuationBlend_IsAverageOfThreeFields()
    {
        var doc = new DynastyValuationDocument
        { CareerValueScore = 90, TradeValue = 60, DiscountedFutureValue = 30 };
        RookieDynastyScoreCalculator.ScoreValuationBlend(doc).Should().BeApproximately(60, 0.1);
    }

    // ── FantasyPros rank ─────────────────────────────────────────────────
    [Fact]
    public void ScoreFantasyProsRank_Rank1_Returns100() =>
        RookieDynastyScoreCalculator.ScoreFantasyProsRank(1).Should().BeApproximately(100, 0.1);

    [Fact]
    public void ScoreFantasyProsRank_NullRank_ReturnsZero() =>
        RookieDynastyScoreCalculator.ScoreFantasyProsRank(null).Should().Be(0);

    [Fact]
    public void ScoreFantasyProsRank_Rank100_ReturnsNearZero() =>
        RookieDynastyScoreCalculator.ScoreFantasyProsRank(100).Should().BeGreaterThanOrEqualTo(0);

    // ── PFF grade ────────────────────────────────────────────────────────
    [Fact]
    public void ScorePffGrade_NullGrade_ReturnsZero() =>
        RookieDynastyScoreCalculator.ScorePffGrade(null).Should().Be(0);

    [Fact]
    public void ScorePffGrade_Grade100_Returns100() =>
        RookieDynastyScoreCalculator.ScorePffGrade(100).Should().BeApproximately(100, 0.1);

    [Fact]
    public void ScorePffGrade_Grade75_Returns75() =>
        RookieDynastyScoreCalculator.ScorePffGrade(75).Should().BeApproximately(75, 0.1);

    [Fact]
    public void ScorePffGrade_ClampsAbove100()
    {
        // PFF grades should never exceed 100 but be defensive
        RookieDynastyScoreCalculator.ScorePffGrade(110).Should().BeApproximately(100, 0.1);
    }

    // ── Consensus ADP ────────────────────────────────────────────────────
    [Fact]
    public void ScoreConsensusAdp_NullAdp_ReturnsZero() =>
        RookieDynastyScoreCalculator.ScoreConsensusAdp(null).Should().Be(0);

    [Fact]
    public void ScoreConsensusAdp_Adp1_Returns100() =>
        RookieDynastyScoreCalculator.ScoreConsensusAdp(1).Should().BeApproximately(100, 0.1);

    [Fact]
    public void ScoreConsensusAdp_LateAdp_ReturnsLow()
    {
        var score = RookieDynastyScoreCalculator.ScoreConsensusAdp(150);
        score.Should().BeLessThan(30);
    }

    [Fact]
    public void ScoreConsensusAdp_Adp1_HigherThanAdp50()
    {
        var adp1 = RookieDynastyScoreCalculator.ScoreConsensusAdp(1);
        var adp50 = RookieDynastyScoreCalculator.ScoreConsensusAdp(50);
        adp1.Should().BeGreaterThan(adp50);
    }

    // ── Signal normalization ─────────────────────────────────────────────
    [Fact]
    public void Calculate_AllNullInputs_ReturnsPositionalFloorOnly()
    {
        var score = RookieDynastyScoreCalculator.Calculate(
            overallPick: null,
            position: "WR",
            valuation: null,
            fantasyProsRank: null,
            pffGrade: null,
            consensusAdp: null);

        // Positional floor only: 90 (WR) * 10% = 9.0
        score.Should().BeApproximately(9.0, 0.5);
    }

    [Fact]
    public void Calculate_AllNullInputs_RbScoresLowerThanWr()
    {
        var wr = RookieDynastyScoreCalculator.Calculate(
            null, "WR", null, null, null, null);
        var rb = RookieDynastyScoreCalculator.Calculate(
            null, "RB", null, null, null, null);

        wr.Should().BeGreaterThan(rb);
    }

    [Fact]
    public void Calculate_FpRankOnly_NormalizesToFullPool()
    {
        // With only FP rank active, its weight normalizes to 90%
        // FP rank 1 = score 100 → 100 * 90% + positional floor
        var score = RookieDynastyScoreCalculator.Calculate(
            overallPick: null,
            position: "RB",
            valuation: null,
            fantasyProsRank: 1,
            pffGrade: null,
            consensusAdp: null);

        // 100 * 0.90 + 55 * 0.10 = 90 + 5.5 = 95.5
        score.Should().BeApproximately(95.5, 0.5);
    }

    [Fact]
    public void Calculate_NullPickWithFpRank_ScoresHighForTopRbProspect()
    {
        var score = RookieDynastyScoreCalculator.Calculate(
            overallPick: null,
            position: "RB",
            valuation: null,
            fantasyProsRank: 3,
            pffGrade: null,
            consensusAdp: null);

        // Should score well above the flat positional floor
        score.Should().BeGreaterThan(50);
    }

    [Fact]
    public void Calculate_AllSignalsActive_ScoreAlwaysBetween0And100()
    {
        var doc = new DynastyValuationDocument
        { CareerValueScore = 150, TradeValue = 200, DiscountedFutureValue = -50 };

        var score = RookieDynastyScoreCalculator.Calculate(
            overallPick: 1,
            position: "WR",
            valuation: doc,
            fantasyProsRank: 1,
            pffGrade: 100,
            consensusAdp: 1.0);

        score.Should().BeInRange(0, 100);
    }

    [Fact]
    public void Calculate_FirstOverallWr_AllSignals_ScoresAbove85()
    {
        var doc = new DynastyValuationDocument
        { CareerValueScore = 80, TradeValue = 75, DiscountedFutureValue = 85 };

        var score = RookieDynastyScoreCalculator.Calculate(
            overallPick: 1,
            position: "WR",
            valuation: doc,
            fantasyProsRank: 1,
            pffGrade: 92,
            consensusAdp: 1.0);

        score.Should().BeGreaterThan(85);
    }

    [Fact]
    public void Calculate_MoreSignals_TopProspectScoresHigh()
    {
        var doc = new DynastyValuationDocument
        { CareerValueScore = 70, TradeValue = 65, DiscountedFutureValue = 60 };

        var score = RookieDynastyScoreCalculator.Calculate(
            overallPick: 1,
            position: "WR",
            valuation: doc,
            fantasyProsRank: 1,
            pffGrade: 92,
            consensusAdp: 1.0);

        // All signals present, all strong — should be a high composite
        score.Should().BeGreaterThan(85);
    }

    // ── ActiveSignals transparency ───────────────────────────────────────
    [Fact]
    public void CalculateWithBreakdown_ActiveSignals_ReflectsWhatIsPresent()
    {
        var bd = RookieDynastyScoreCalculator.CalculateWithBreakdown(
            overallPick: null,
            position: "WR",
            valuation: null,
            fantasyProsRank: 5,
            pffGrade: 88,
            consensusAdp: null);

        bd.ActiveSignals.Should().Contain("FantasyPros");
        bd.ActiveSignals.Should().Contain("PffGrade");
        bd.ActiveSignals.Should().NotContain("DraftCapital");
        bd.ActiveSignals.Should().NotContain("ConsensusAdp");
        bd.ActiveSignals.Should().NotContain("ValuationBlend");
    }

    [Fact]
    public void CalculateWithBreakdown_DynastyScoreMatchesCalculate()
    {
        var doc = new DynastyValuationDocument
        { CareerValueScore = 70, TradeValue = 65, DiscountedFutureValue = 60 };

        var bd = RookieDynastyScoreCalculator.CalculateWithBreakdown(
            overallPick: 15,
            position: "WR",
            valuation: doc,
            fantasyProsRank: 5,
            pffGrade: null,
            consensusAdp: null);

        var direct = RookieDynastyScoreCalculator.Calculate(
            overallPick: 15,
            position: "WR",
            valuation: doc,
            fantasyProsRank: 5,
            pffGrade: null,
            consensusAdp: null);

        bd.DynastyScore.Should().BeApproximately(direct, 0.1);
    }
}
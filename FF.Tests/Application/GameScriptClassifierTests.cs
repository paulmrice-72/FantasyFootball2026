// FF.Tests/Application/GameScriptClassifierTests.cs
using FF.Application.Services;
using FF.Domain.Enums;
using FluentAssertions;

namespace FF.Tests.Application;

public class GameScriptClassifierTests
{
    // ── Classification Tests ──────────────────────────────────────────────

    [Fact]
    public void Classify_BlowoutWin_WhenFavouredByTenOrMore()
    {
        var result = GameScriptClassifier.Classify(spread: 10m);
        result.Script.Should().Be(GameScript.BlowoutWin);
    }

    [Fact]
    public void Classify_BlowoutWin_WhenFavouredByMore()
    {
        var result = GameScriptClassifier.Classify(spread: 14m);
        result.Script.Should().Be(GameScript.BlowoutWin);
    }

    [Fact]
    public void Classify_Competitive_WhenSpreadWithinSeven()
    {
        var result = GameScriptClassifier.Classify(spread: 3m);
        result.Script.Should().Be(GameScript.Competitive);
    }

    [Fact]
    public void Classify_Competitive_WhenSpreadIsZero()
    {
        var result = GameScriptClassifier.Classify(spread: 0m);
        result.Script.Should().Be(GameScript.Competitive);
    }

    [Fact]
    public void Classify_Competitive_WhenSpreadIsNegativeWithinSeven()
    {
        var result = GameScriptClassifier.Classify(spread: -6m);
        result.Script.Should().Be(GameScript.Competitive);
    }

    [Fact]
    public void Classify_Trailing_WhenUnderdogByTenOrMore()
    {
        var result = GameScriptClassifier.Classify(spread: -10m);
        result.Script.Should().Be(GameScript.Trailing);
    }

    [Fact]
    public void Classify_Trailing_WhenUnderdogByMore()
    {
        var result = GameScriptClassifier.Classify(spread: -17m);
        result.Script.Should().Be(GameScript.Trailing);
    }

    // ── Multiplier Tests ──────────────────────────────────────────────────

    [Fact]
    public void Classify_BlowoutWin_BoostsRbMultiplier()
    {
        var result = GameScriptClassifier.Classify(spread: 10m);
        result.RbVolumeMultiplier.Should().BeGreaterThan(1.0m,
            "RB volume increases in blowout wins");
    }

    [Fact]
    public void Classify_BlowoutWin_CutsWrTeMultiplier()
    {
        var result = GameScriptClassifier.Classify(spread: 10m);
        result.WrTeVolumeMultiplier.Should().BeLessThan(1.0m,
            "WR/TE volume decreases in blowout wins");
    }

    [Fact]
    public void Classify_Trailing_CutsRbMultiplier()
    {
        var result = GameScriptClassifier.Classify(spread: -10m);
        result.RbVolumeMultiplier.Should().BeLessThan(1.0m,
            "RB volume decreases when trailing");
    }

    [Fact]
    public void Classify_Trailing_BoostsWrTeMultiplier()
    {
        var result = GameScriptClassifier.Classify(spread: -10m);
        result.WrTeVolumeMultiplier.Should().BeGreaterThan(1.0m,
            "WR/TE volume increases when trailing");
    }

    [Fact]
    public void Classify_Competitive_NeutralMultipliers()
    {
        var result = GameScriptClassifier.Classify(spread: 0m);
        result.RbVolumeMultiplier.Should().Be(1.0m);
        result.WrTeVolumeMultiplier.Should().Be(1.0m);
    }

    [Fact]
    public void Classify_Trailing_HigherCorrelationCoefficient()
    {
        // Trailing teams pass more — QB/WR1 correlation increases
        var trailing = GameScriptClassifier.Classify(spread: -10m);
        var blowout = GameScriptClassifier.Classify(spread: 10m);

        trailing.QbWr1CorrelationCoefficient.Should().BeGreaterThan(
            blowout.QbWr1CorrelationCoefficient,
            "trailing teams pass more, increasing QB/WR1 correlation");
    }

    // ── ApplyMultiplier Tests ─────────────────────────────────────────────

    [Fact]
    public void ApplyMultiplier_RB_BlowoutWin_IncreasesProjection()
    {
        var correlation = GameScriptClassifier.Classify(spread: 10m);
        var adjusted = GameScriptClassifier.ApplyMultiplier(20m, "RB", correlation);

        adjusted.Should().BeGreaterThan(20m,
            "RB projection should increase in a blowout win");
        adjusted.Should().BeApproximately(22.4m, 0.01m); // 20 * 1.12
    }

    [Fact]
    public void ApplyMultiplier_WR_Trailing_IncreasesProjection()
    {
        var correlation = GameScriptClassifier.Classify(spread: -10m);
        var adjusted = GameScriptClassifier.ApplyMultiplier(15m, "WR", correlation);

        adjusted.Should().BeGreaterThan(15m,
            "WR projection should increase when team is trailing");
        adjusted.Should().BeApproximately(16.8m, 0.01m); // 15 * 1.12
    }

    [Fact]
    public void ApplyMultiplier_QB_UnaffectedByScript()
    {
        var blowout = GameScriptClassifier.Classify(spread: 10m);
        var trailing = GameScriptClassifier.Classify(spread: -10m);

        GameScriptClassifier.ApplyMultiplier(30m, "QB", blowout)
            .Should().Be(30m, "QB projection is not adjusted by game script");

        GameScriptClassifier.ApplyMultiplier(30m, "QB", trailing)
            .Should().Be(30m, "QB projection is not adjusted by game script");
    }

    [Fact]
    public void ApplyMultiplier_NeverReturnsNegative()
    {
        var correlation = GameScriptClassifier.Classify(spread: -10m);
        var adjusted = GameScriptClassifier.ApplyMultiplier(0m, "RB", correlation);

        adjusted.Should().Be(0m, "projection can never go below zero");
    }

    [Fact]
    public void Classify_SpreadStoredOnResult()
    {
        var result = GameScriptClassifier.Classify(spread: 7.5m);
        result.Spread.Should().Be(7.5m);
    }
}
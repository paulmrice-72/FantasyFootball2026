using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using FF.Domain.Entities;
using FF.Domain.Enums;
using FF.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FF.Tests.Application.Dynasty;

public class BreakoutDetectionServiceTests
{
    private readonly Mock<IPlayerRepository> _playerRepo = new();
    private readonly Mock<IPlayerUsageMetricsRepository> _metricsRepo = new();
    private readonly Mock<ICareerSimulationRepository> _careerRepo = new();

    private BreakoutDetectionService CreateSut() => new(
        _playerRepo.Object,
        _metricsRepo.Object,
        _careerRepo.Object,
        NullLogger<BreakoutDetectionService>.Instance);

    private static Player MakePlayer(
        string sleeperId, string pos, int age, int yearsExp,
        string? gsisId = null)
    {
        var position = Enum.Parse<Position>(pos);
        var p = Player.Create("Test", "Player", position,
            sleeperPlayerId: sleeperId, gsisId: gsisId);
        p.UpdateFields("Test", "Player", position, age, yearsExp, 88, gsisId);
        return p;
    }

    private static PlayerUsageMetricsDocument MakeMetrics(
        string playerId, string pos,
        decimal targetShare3Wk = 0.18m, decimal targetShareSeason = 0.15m,
        decimal snapPct3Wk = 0.80m, decimal snapPctSeason = 0.75m,
        decimal wopr3Wk = 0.35m, decimal woprSeason = 0.30m,
        decimal adot3Wk = 10m, decimal adotSeason = 8m,
        decimal carryShare3Wk = 0.25m, decimal carryShareSeason = 0.20m) =>
        new()
        {
            PlayerId = playerId,
            Position = pos,
            TargetShare3Wk = targetShare3Wk,
            TargetShareSeason = targetShareSeason,
            SnapPct3Wk = snapPct3Wk,
            SnapPctSeason = snapPctSeason,
            Wopr3Wk = wopr3Wk,
            WoprSeason = woprSeason,
            ADot3Wk = adot3Wk,
            ADotSeason = adotSeason,
            CarryShare3Wk = carryShare3Wk,
            CarryShareSeason = carryShareSeason
        };

    // ── ScorePlayer — classification thresholds ───────────────────────────

    [Fact]
    public void ScorePlayer_YoungPlayerInBreakoutWindow_ClassifiesAsBreakout()
    {
        var player = MakePlayer("s1", "WR", 23, 2, "g1");
        var metrics = MakeMetrics("g1", "WR",
            targetShare3Wk: 0.22m, targetShareSeason: 0.14m,   // surging usage
            snapPct3Wk: 0.88m, snapPctSeason: 0.72m,           // expanding snaps
            wopr3Wk: 0.42m, woprSeason: 0.28m,                 // WOPR up
            adot3Wk: 12m, adotSeason: 9m);                     // deeper routes

        var sut = CreateSut();
        var result = sut.ScorePlayer(player, metrics, null);

        result.Classification.Should().Be(BreakoutClassification.Breakout);
        result.Score.Should().BeGreaterThanOrEqualTo(65);
        result.Signals.Should().NotBeEmpty();
    }

    [Fact]
    public void ScorePlayer_PrimePlayerStableUsage_ClassifiesAsOnCurve()
    {
        var player = MakePlayer("s1", "WR", 26, 4, "g1");
        var metrics = MakeMetrics("g1", "WR",
            targetShare3Wk: 0.20m, targetShareSeason: 0.20m,   // flat usage
            snapPct3Wk: 0.80m, snapPctSeason: 0.80m);          // flat snaps

        var sut = CreateSut();
        var result = sut.ScorePlayer(player, metrics, null);

        result.Classification.Should().Be(BreakoutClassification.OnCurve);
    }

    [Fact]
    public void ScorePlayer_OldPlayerDecliningUsage_ScoresLow()
    {
        var player = MakePlayer("s1", "RB", 30, 8, "g1");
        var metrics = MakeMetrics("g1", "RB",
            carryShare3Wk: 0.12m, carryShareSeason: 0.28m,
            snapPct3Wk: 0.40m, snapPctSeason: 0.65m);

        var sut = CreateSut();
        var result = sut.ScorePlayer(player, metrics, null);

        result.Score.Should().BeLessThan(25);
        result.Classification.Should().BeOneOf(
            BreakoutClassification.Declining,
            BreakoutClassification.Unknown);
        result.Signals.Should().Contain(s => s.Contains("declining") || s.Contains("Snap%"));
    }

    [Fact]
    public void ScorePlayer_NoMetrics_ReturnsValidResultWithoutCrashing()
    {
        var player = MakePlayer("s1", "TE", 24, 2);

        var sut = CreateSut();
        var result = sut.ScorePlayer(player, null, null);

        result.Should().NotBeNull();
        result.Score.Should().BeInRange(0, 100);
        result.Classification.Should().NotBe(BreakoutClassification.Breakout); // needs metrics
    }

    [Fact]
    public void ScorePlayer_NoAge_ReturnsUnknown()
    {
        var player = Player.Create("No", "Age", Position.WR, sleeperPlayerId: "s1");
        // Age not set — remains null

        var sut = CreateSut();
        var result = sut.ScorePlayer(player, null, null);

        result.Classification.Should().Be(BreakoutClassification.Unknown);
        result.Score.Should().Be(0);
    }

    // ── Score ordering ────────────────────────────────────────────────────

    [Fact]
    public void ScorePlayer_YoungBreakoutWr_ScoresHigherThan_OldDeclingRb()
    {
        var youngWr = MakePlayer("s1", "WR", 23, 2, "g1");
        var oldRb = MakePlayer("s2", "RB", 31, 9, "g2");

        var goodMetrics = MakeMetrics("g1", "WR",
            targetShare3Wk: 0.22m, targetShareSeason: 0.14m,
            snapPct3Wk: 0.88m, snapPctSeason: 0.70m);

        var badMetrics = MakeMetrics("g2", "RB",
            carryShare3Wk: 0.10m, carryShareSeason: 0.30m,
            snapPct3Wk: 0.35m, snapPctSeason: 0.60m);

        var sut = CreateSut();
        var wrResult = sut.ScorePlayer(youngWr, goodMetrics, null);
        var rbResult = sut.ScorePlayer(oldRb, badMetrics, null);

        wrResult.Score.Should().BeGreaterThan(rbResult.Score);
    }

    // ── Signal content ────────────────────────────────────────────────────

    [Fact]
    public void ScorePlayer_RisingUsage_IncludesUsageSignal()
    {
        var player = MakePlayer("s1", "WR", 24, 3, "g1");
        var metrics = MakeMetrics("g1", "WR",
            targetShare3Wk: 0.25m, targetShareSeason: 0.15m);  // +10% surge

        var sut = CreateSut();
        var result = sut.ScorePlayer(player, metrics, null);

        result.Signals.Should().Contain(s => s.Contains("Usage rising"));
    }

    [Fact]
    public void ScorePlayer_Year2Player_IncludesBreakoutWindowSignal()
    {
        var player = MakePlayer("s1", "WR", 23, 2, "g1");
        var metrics = MakeMetrics("g1", "WR");

        var sut = CreateSut();
        var result = sut.ScorePlayer(player, metrics, null);

        result.Signals.Should().Contain(s => s.Contains("breakout window"));
    }

    // ── Score bounds ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("QB", 25, 3)]
    [InlineData("RB", 22, 1)]
    [InlineData("WR", 28, 6)]
    [InlineData("TE", 26, 4)]
    public void ScorePlayer_AllPositions_ScoreAlwaysInBounds(
        string pos, int age, int exp)
    {
        var player = MakePlayer("s1", pos, age, exp, "g1");
        var metrics = MakeMetrics("g1", pos);

        var sut = CreateSut();
        var result = sut.ScorePlayer(player, metrics, null);

        result.Score.Should().BeInRange(0, 100);
    }
}
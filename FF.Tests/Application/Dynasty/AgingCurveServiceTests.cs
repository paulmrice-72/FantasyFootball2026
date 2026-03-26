using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Domain.Entities;
using FF.Domain.Enums;
using FF.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FF.Tests.Application.Dynasty;

public class AgingCurveServiceTests
{
    private readonly Mock<IPlayerGameLogRepository> _gameLogRepo = new();
    private readonly Mock<IPlayerRepository> _playerRepo = new();

    private IAgingCurveService CreateSut() =>
        new AgingCurveService(_gameLogRepo.Object, _playerRepo.Object,
            NullLogger<AgingCurveService>.Instance);

    // ── EvaluateAtAge ─────────────────────────────────────────────────────

    [Fact]
    public void EvaluateAtAge_ReturnsStoredValue_WhenAgeInMap()
    {
        var curve = new AgingCurveDocument
        {
            Position = "WR",
            AgeValueMap = new Dictionary<int, double> { [26] = 95.0 },
            Coefficients = []
        };

        var sut = CreateSut();
        var result = sut.EvaluateAtAge(curve, 26);

        result.Should().BeApproximately(95.0, 0.01);
    }

    [Fact]
    public void EvaluateAtAge_ReturnsValueBetween0And100_ForAnyAge()
    {
        var curve = new AgingCurveDocument
        {
            Position = "RB",
            AgeValueMap = [],
            Coefficients = [500.0, -50.0, 1.5, -0.01]  // arbitrary polynomial
        };

        var sut = CreateSut();

        for (int age = 18; age <= 45; age++)
        {
            var result = sut.EvaluateAtAge(curve, age);
            result.Should().BeGreaterThanOrEqualTo(0).And.BeLessThanOrEqualTo(100);
        }
    }

    // ── GetAgeMultiplierAsync ─────────────────────────────────────────────

    [Theory]
    [InlineData("RB", 24, true)]   // peak age — should be highest multiplier
    [InlineData("RB", 21, false)]  // ascending — below peak
    [InlineData("RB", 32, false)]  // declining — below peak
    [InlineData("QB", 29, true)]   // QB peak
    public async Task GetAgeMultiplierAsync_PeakAgeHasHighestMultiplier(
        string position, int age, bool isPeak)
    {
        var sut = CreateSut();
        var multiplier = await sut.GetAgeMultiplierAsync(position, age);

        multiplier.Should().BeInRange(0.0, 1.0);

        if (isPeak)
            multiplier.Should().BeApproximately(1.0, 0.01);
        else
            multiplier.Should().BeLessThan(1.0);
    }

    // ── BuildAllCurvesAsync — default curve fallback ──────────────────────

    [Fact]
    public async Task BuildAllCurvesAsync_ReturnsDefaultCurves_WhenInsufficientData()
    {
        _gameLogRepo
            .Setup(r => r.GetByPositionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _playerRepo
            .Setup(r => r.GetByPositionAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateSut();
        var curves = await sut.BuildAllCurvesAsync();

        curves.Should().HaveCount(4);
        curves.Should().AllSatisfy(c => c.IsDefaultCurve.Should().BeTrue());
        curves.Select(c => c.Position).Should().BeEquivalentTo(["QB", "RB", "WR", "TE"]);
    }

    [Fact]
    public async Task BuildAllCurvesAsync_DefaultCurves_HaveValidAgeValueMaps()
    {
        _gameLogRepo
            .Setup(r => r.GetByPositionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _playerRepo
            .Setup(r => r.GetByPositionAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateSut();
        var curves = await sut.BuildAllCurvesAsync();

        foreach (var curve in curves)
        {
            curve.AgeValueMap.Should().NotBeEmpty();
            curve.AgeValueMap.Values.Should().AllSatisfy(
                v => v.Should().BeInRange(0.0, 100.0));
            curve.PeakAge.Should().BeGreaterThan(20).And.BeLessThan(42);
        }
    }

    [Fact]
    public async Task BuildAllCurvesAsync_RbPeakAge_IsEarlierThanQbPeakAge()
    {
        _gameLogRepo
            .Setup(r => r.GetByPositionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _playerRepo
            .Setup(r => r.GetByPositionAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateSut();
        var curves = await sut.BuildAllCurvesAsync();

        var rb = curves.First(c => c.Position == "RB");
        var qb = curves.First(c => c.Position == "QB");

        rb.PeakAge.Should().BeLessThan(qb.PeakAge);
    }

    // ── BuildAllCurvesAsync — real data path ──────────────────────────────

    [Fact]
    public async Task BuildAllCurvesAsync_WithSufficientLogs_ProducesValidCurve()
    {
        // Spread logs across 5 seasons to get multiple age buckets
        // Player is 28 in 2026 → ages 22-26 across seasons 2020-2024
        var logs = new List<PlayerGameLogDocument>();
        for (int season = 2020; season <= 2024; season++)
        {
            for (int week = 1; week <= 17; week++)
            {
                logs.Add(new PlayerGameLogDocument
                {
                    PlayerId = "p1",
                    SleeperPlayerId = "sleeper-1",
                    Position = "WR",
                    Season = season,
                    Week = week,
                    FantasyPointsPpr = 10m + (week % 5)
                });
            }
        }

        _gameLogRepo
            .Setup(r => r.GetByPositionAsync("WR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(logs);

        _gameLogRepo
            .Setup(r => r.GetByPositionAsync(It.Is<string>(s => s != "WR"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var mockPlayer = Player.Create("Test", "Player", Position.WR,
            sleeperPlayerId: "sleeper-1");
        mockPlayer.UpdateFields("Test", "Player", Position.WR, 28, 6, 88);

        _playerRepo
            .Setup(r => r.GetByPositionAsync(Position.WR, It.IsAny<CancellationToken>()))
            .ReturnsAsync([mockPlayer]);

        _playerRepo
            .Setup(r => r.GetByPositionAsync(It.Is<Position>(p => p != Position.WR), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateSut();
        var curves = await sut.BuildAllCurvesAsync();

        var wr = curves.First(c => c.Position == "WR");

        wr.Should().NotBeNull();
        wr.AgeValueMap.Should().NotBeEmpty();
        wr.AgeValueMap.Values.Should().AllSatisfy(v => v.Should().BeInRange(0.0, 100.0));
        wr.SampleSize.Should().Be(logs.Count);     // real data path — SampleSize = log count
        wr.IsDefaultCurve.Should().BeFalse();
        wr.Coefficients.Should().HaveCount(4);     // degree-3 polynomial
    }
}
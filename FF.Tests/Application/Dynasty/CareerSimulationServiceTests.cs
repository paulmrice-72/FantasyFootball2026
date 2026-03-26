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

public class CareerSimulationServiceTests
{
    private readonly Mock<IPlayerRepository> _playerRepo = new();
    private readonly Mock<IAgingCurveRepository> _agingCurveRepo = new();
    private readonly Mock<ISimulationResultRepository> _simResultRepo = new();

    private CareerSimulationService CreateSut() => new(
        _playerRepo.Object,
        _agingCurveRepo.Object,
        _simResultRepo.Object,
        NullLogger<CareerSimulationService>.Instance);

    private static Player MakePlayer(
        string sleeperId, string pos, int age, string first = "Test", string last = "Player")
    {
        var position = Enum.Parse<Position>(pos);
        var p = Player.Create(first, last, position, sleeperPlayerId: sleeperId);
        p.UpdateFields(first, last, position, age, age - 22, 88);
        return p;
    }

    private void SetupNoSimResults() =>
        _simResultRepo
            .Setup(r => r.GetMostRecentBySleeperIdAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SimulationResultDocument?)null);

    private void SetupNoCurve() =>
        _agingCurveRepo
            .Setup(r => r.GetByPositionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgingCurveDocument?)null);

    // ── SimulateAllPlayersAsync ───────────────────────────────────────────

    [Fact]
    public async Task SimulateAllPlayersAsync_ReturnsOneDocPerEligiblePlayer()
    {
        var players = new List<Player>
        {
            MakePlayer("s1", "WR", 24),
            MakePlayer("s2", "WR", 27)
        };

        _playerRepo
            .Setup(r => r.GetByPositionAsync(Position.WR, It.IsAny<CancellationToken>()))
            .ReturnsAsync(players);
        _playerRepo
            .Setup(r => r.GetByPositionAsync(It.Is<Position>(p => p != Position.WR), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        SetupNoCurve();
        SetupNoSimResults();

        var sut = CreateSut();
        var result = await sut.SimulateAllPlayersAsync(2026);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(r => r.SleeperPlayerId.Should().NotBeNullOrEmpty());
    }

    [Fact]
    public async Task SimulateAllPlayersAsync_SkipsPlayersWithNoSleeperId()
    {
        var withId = MakePlayer("s1", "RB", 23);
        var withoutId = Player.Create("No", "Id", Position.RB);  // no sleeperId

        _playerRepo
            .Setup(r => r.GetByPositionAsync(Position.RB, It.IsAny<CancellationToken>()))
            .ReturnsAsync([withId, withoutId]);
        _playerRepo
            .Setup(r => r.GetByPositionAsync(It.Is<Position>(p => p != Position.RB), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        SetupNoCurve();
        SetupNoSimResults();

        var sut = CreateSut();
        var result = await sut.SimulateAllPlayersAsync(2026);

        result.Should().HaveCount(1);
        result[0].SleeperPlayerId.Should().Be("s1");
    }

    // ── Career document structure ─────────────────────────────────────────

    [Fact]
    public async Task SimulateAllPlayersAsync_ProducesCorrectYearCount()
    {
        _playerRepo
            .Setup(r => r.GetByPositionAsync(Position.QB, It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakePlayer("qb1", "QB", 28)]);
        _playerRepo
            .Setup(r => r.GetByPositionAsync(It.Is<Position>(p => p != Position.QB), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        SetupNoCurve();
        SetupNoSimResults();

        var sut = CreateSut();
        var result = await sut.SimulateAllPlayersAsync(2026);

        result[0].YearProjections.Should().HaveCount(5);
    }

    [Fact]
    public async Task SimulateAllPlayersAsync_YearProjections_HaveAscendingYears()
    {
        _playerRepo
            .Setup(r => r.GetByPositionAsync(Position.WR, It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakePlayer("wr1", "WR", 25)]);
        _playerRepo
            .Setup(r => r.GetByPositionAsync(It.Is<Position>(p => p != Position.WR), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        SetupNoCurve();
        SetupNoSimResults();

        var sut = CreateSut();
        var result = await sut.SimulateAllPlayersAsync(2026);

        var years = result[0].YearProjections.Select(y => y.Year).ToList();
        years.Should().BeInAscendingOrder();
        years[0].Should().Be(2026);
        years[4].Should().Be(2030);
    }

    [Fact]
    public async Task SimulateAllPlayersAsync_FloorAlwaysLessThanCeiling()
    {
        _playerRepo
            .Setup(r => r.GetByPositionAsync(Position.TE, It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakePlayer("te1", "TE", 26)]);
        _playerRepo
            .Setup(r => r.GetByPositionAsync(It.Is<Position>(p => p != Position.TE), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        SetupNoCurve();
        SetupNoSimResults();

        var sut = CreateSut();
        var result = await sut.SimulateAllPlayersAsync(2026);

        result[0].YearProjections.Should().AllSatisfy(y =>
            y.FloorFppg.Should().BeLessThanOrEqualTo(y.CeilingFppg));
    }

    // ── Aging effects ─────────────────────────────────────────────────────

    [Fact]
    public async Task SimulateAllPlayersAsync_OlderRb_HasHigherInjuryRisk()
    {
        _playerRepo
            .Setup(r => r.GetByPositionAsync(Position.RB, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakePlayer("young", "RB", 22, "Young", "Back"),
                MakePlayer("old",   "RB", 30, "Old",   "Back")
            ]);
        _playerRepo
            .Setup(r => r.GetByPositionAsync(It.Is<Position>(p => p != Position.RB), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        SetupNoCurve();
        SetupNoSimResults();

        var sut = CreateSut();
        var result = await sut.SimulateAllPlayersAsync(2026);

        var youngFirstYear = result.First(r => r.SleeperPlayerId == "young").YearProjections[0];
        var oldFirstYear = result.First(r => r.SleeperPlayerId == "old").YearProjections[0];

        oldFirstYear.InjuryRisk.Should().BeGreaterThan(youngFirstYear.InjuryRisk);
    }

    [Fact]
    public async Task SimulateAllPlayersAsync_CareerValueScore_IsPositive()
    {
        _playerRepo
            .Setup(r => r.GetByPositionAsync(Position.WR, It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakePlayer("wr1", "WR", 24)]);
        _playerRepo
            .Setup(r => r.GetByPositionAsync(It.Is<Position>(p => p != Position.WR), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        SetupNoCurve();
        SetupNoSimResults();

        var sut = CreateSut();
        var result = await sut.SimulateAllPlayersAsync(2026);

        result[0].CareerValueScore.Should().BeGreaterThan(0);
    }

    // ── Career phase classification ───────────────────────────────────────

    [Theory]
    [InlineData("RB", 21, CareerPhase.Ascending)]
    [InlineData("RB", 24, CareerPhase.Prime)]
    [InlineData("RB", 28, CareerPhase.Declining)]
    [InlineData("QB", 28, CareerPhase.Prime)]
    [InlineData("QB", 22, CareerPhase.Ascending)]
    public async Task SimulateAllPlayersAsync_CareerPhase_CorrectForAge(
        string pos, int age, CareerPhase expected)
    {
        var posEnum = Enum.Parse<Position>(pos);

        _playerRepo
            .Setup(r => r.GetByPositionAsync(posEnum, It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakePlayer("p1", pos, age)]);
        _playerRepo
            .Setup(r => r.GetByPositionAsync(It.Is<Position>(p => p != posEnum), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        SetupNoCurve();
        SetupNoSimResults();

        var sut = CreateSut();
        var result = await sut.SimulateAllPlayersAsync(2026);

        result[0].CareerPhase.Should().Be(expected);
    }
}
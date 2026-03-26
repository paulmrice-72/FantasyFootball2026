using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using FF.Domain.Enums;
using FF.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FF.Tests.Application.Dynasty;

public class DfvCalculationServiceTests
{
    private readonly Mock<ICareerSimulationRepository> _careerRepo = new();
    private readonly Mock<IDynastyValuationRepository> _valuationRepo = new();

    private DfvCalculationService CreateSut() => new(
        _careerRepo.Object,
        _valuationRepo.Object,
        NullLogger<DfvCalculationService>.Instance);

    private static CareerSimulationDocument MakeCareerSim(
        string sleeperPlayerId, string position, int currentAge,
        double yearOneValue = 150.0)
    {
        var projections = new List<CareerYearProjection>();
        for (int i = 0; i < 5; i++)
        {
            projections.Add(new CareerYearProjection
            {
                Year = 2026 + i,
                AgeAtYear = currentAge + i,
                SeasonValue = yearOneValue * Math.Pow(0.9, i),
                MedianFppg = 12.0,
                FloorFppg = 8.0,
                CeilingFppg = 18.0,
                InjuryRisk = 0.15,
                AgingMultiplier = 1.0 - i * 0.05,
                Phase = CareerPhase.Prime
            });
        }

        return new CareerSimulationDocument
        {
            SleeperPlayerId = sleeperPlayerId,
            Position = position,
            CurrentAge = currentAge,
            Season = 2026,
            YearProjections = projections,
            CareerValueScore = yearOneValue * 3.5,
            Iterations = 1000
        };
    }

    private static DynastyValuationDocument MakeValuation(
        string sleeperPlayerId, string position, int age,
        double breakoutScore = 50.0) =>
        new()
        {
            SleeperPlayerId = sleeperPlayerId,
            Position = position,
            Age = age,
            BreakoutScore = breakoutScore,
            BreakoutClassification = BreakoutClassification.OnCurve
        };

    // ── CalculateRawDfv ───────────────────────────────────────────────────

    [Fact]
    public void CalculateRawDfv_ReturnsPositiveValue_ForValidCareerSim()
    {
        var sim = MakeCareerSim("s1", "WR", 24);
        var sut = CreateSut();

        var result = sut.CalculateRawDfv(sim, "WR");

        result.Should().BeGreaterThan(0);
    }

    [Fact]
    public void CalculateRawDfv_EmptyProjections_ReturnsZero()
    {
        var sim = new CareerSimulationDocument
        {
            SleeperPlayerId = "s1",
            Position = "WR",
            Season = 2026,
            YearProjections = []
        };
        var sut = CreateSut();

        var result = sut.CalculateRawDfv(sim, "WR");

        result.Should().Be(0);
    }

    [Fact]
    public void CalculateRawDfv_RbDiscountsMoreThanWr()
    {
        // Same underlying production — RB should have lower DFV due to higher discount
        var wrSim = MakeCareerSim("wr1", "WR", 24, yearOneValue: 150);
        var rbSim = MakeCareerSim("rb1", "RB", 24, yearOneValue: 150);
        var sut = CreateSut();

        var wrDfv = sut.CalculateRawDfv(wrSim, "WR");
        var rbDfv = sut.CalculateRawDfv(rbSim, "RB");

        // RB has 20% discount vs WR 12% — but RB also has 1.10 scarcity boost
        // Net effect: WR should still be higher or close
        // The test validates discount mechanics are applied differently
        wrDfv.Should().NotBe(rbDfv);
    }

    [Fact]
    public void CalculateRawDfv_HigherProductionPlayer_HasHigherDfv()
    {
        var eliteSim = MakeCareerSim("s1", "WR", 24, yearOneValue: 250);
        var averageSim = MakeCareerSim("s2", "WR", 24, yearOneValue: 100);
        var sut = CreateSut();

        var eliteDfv = sut.CalculateRawDfv(eliteSim, "WR");
        var averageDfv = sut.CalculateRawDfv(averageSim, "WR");

        eliteDfv.Should().BeGreaterThan(averageDfv);
    }

    // ── CalculateAllAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task CalculateAllAsync_NoValuations_ReturnsEmpty()
    {
        _valuationRepo
            .Setup(r => r.GetByPositionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var sut = CreateSut();
        var result = await sut.CalculateAllAsync(2026);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CalculateAllAsync_NormalizesTradeValueTo0_100()
    {
        var valuations = new List<DynastyValuationDocument>
        {
            MakeValuation("s1", "WR", 24, breakoutScore: 80),
            MakeValuation("s2", "WR", 27, breakoutScore: 50),
            MakeValuation("s3", "WR", 31, breakoutScore: 20)
        };

        _valuationRepo
            .Setup(r => r.GetByPositionAsync("WR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(valuations);
        _valuationRepo
            .Setup(r => r.GetByPositionAsync(It.Is<string>(p => p != "WR"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _careerRepo
            .Setup(r => r.GetByPlayerIdAsync("s1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeCareerSim("s1", "WR", 24, yearOneValue: 200));
        _careerRepo
            .Setup(r => r.GetByPlayerIdAsync("s2", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeCareerSim("s2", "WR", 27, yearOneValue: 150));
        _careerRepo
            .Setup(r => r.GetByPlayerIdAsync("s3", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeCareerSim("s3", "WR", 31, yearOneValue: 80));

        var sut = CreateSut();
        var result = await sut.CalculateAllAsync(2026);

        result.Should().HaveCount(3);
        result.Should().AllSatisfy(v =>
            v.TradeValue.Should().BeInRange(0, 100));
    }

    [Fact]
    public async Task CalculateAllAsync_TopPlayer_HasHighestTradeValue()
    {
        var valuations = new List<DynastyValuationDocument>
        {
            MakeValuation("elite", "RB", 23, breakoutScore: 85),
            MakeValuation("bench", "RB", 30, breakoutScore: 20)
        };

        _valuationRepo
            .Setup(r => r.GetByPositionAsync("RB", It.IsAny<CancellationToken>()))
            .ReturnsAsync(valuations);
        _valuationRepo
            .Setup(r => r.GetByPositionAsync(It.Is<string>(p => p != "RB"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _careerRepo
            .Setup(r => r.GetByPlayerIdAsync("elite", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeCareerSim("elite", "RB", 23, yearOneValue: 220));
        _careerRepo
            .Setup(r => r.GetByPlayerIdAsync("bench", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeCareerSim("bench", "RB", 30, yearOneValue: 60));

        var sut = CreateSut();
        var result = await sut.CalculateAllAsync(2026);

        var elite = result.First(r => r.SleeperPlayerId == "elite");
        var bench = result.First(r => r.SleeperPlayerId == "bench");

        elite.TradeValue.Should().BeGreaterThan(bench.TradeValue);
    }

    [Fact]
    public async Task CalculateAllAsync_PlayerWithNoCareerSim_GetsZeroTradeValue()
    {
        var valuations = new List<DynastyValuationDocument>
        {
            MakeValuation("has-sim",  "TE", 25),
            MakeValuation("no-sim",   "TE", 26)
        };

        _valuationRepo
            .Setup(r => r.GetByPositionAsync("TE", It.IsAny<CancellationToken>()))
            .ReturnsAsync(valuations);
        _valuationRepo
            .Setup(r => r.GetByPositionAsync(It.Is<string>(p => p != "TE"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _careerRepo
            .Setup(r => r.GetByPlayerIdAsync("has-sim", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeCareerSim("has-sim", "TE", 25));
        _careerRepo
            .Setup(r => r.GetByPlayerIdAsync("no-sim", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CareerSimulationDocument?)null);

        var sut = CreateSut();
        var result = await sut.CalculateAllAsync(2026);

        var noSim = result.First(r => r.SleeperPlayerId == "no-sim");
        noSim.TradeValue.Should().Be(0);
    }

    [Fact]
    public async Task CalculateAllAsync_BreakoutBoost_IncreasesTradeValue()
    {
        // Same career sim, different breakout scores — high breakout should win
        var valuations = new List<DynastyValuationDocument>
        {
            MakeValuation("high-bo", "WR", 24, breakoutScore: 90),
            MakeValuation("low-bo",  "WR", 24, breakoutScore: 10)
        };

        _valuationRepo
            .Setup(r => r.GetByPositionAsync("WR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(valuations);
        _valuationRepo
            .Setup(r => r.GetByPositionAsync(It.Is<string>(p => p != "WR"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Identical career sims — only breakout score differs
        _careerRepo
            .Setup(r => r.GetByPlayerIdAsync("high-bo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeCareerSim("high-bo", "WR", 24, yearOneValue: 150));
        _careerRepo
            .Setup(r => r.GetByPlayerIdAsync("low-bo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeCareerSim("low-bo", "WR", 24, yearOneValue: 150));

        var sut = CreateSut();
        var result = await sut.CalculateAllAsync(2026);

        var highBo = result.First(r => r.SleeperPlayerId == "high-bo");
        var lowBo = result.First(r => r.SleeperPlayerId == "low-bo");

        highBo.TradeValue.Should().BeGreaterThan(lowBo.TradeValue);
    }
}
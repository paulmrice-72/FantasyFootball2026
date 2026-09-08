using FF.Application.Interfaces.Persistence;
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
    private readonly Mock<IFantasyProsRookieRankingRepository> _fpRookieRepo = new();

    private DfvCalculationService CreateSut()
    {
        _fpRookieRepo
            .Setup(r => r.GetAllBySeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        return new(
            _careerRepo.Object,
            _valuationRepo.Object,
            _fpRookieRepo.Object,
            NullLogger<DfvCalculationService>.Instance);
    }

    private static CareerSimulationDocument MakeCareerSim(
        string sleeperPlayerId, string position, int currentAge, double yearOneValue = 150.0)
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
        double breakoutScore = 50.0, string nflTeam = "SF", int yearsExperience = 3)
        => new()
        {
            SleeperPlayerId = sleeperPlayerId,
            Position = position,
            Age = age,
            NflTeam = nflTeam,
            BreakoutScore = breakoutScore,
            BreakoutClassification = BreakoutClassification.OnCurve,
            YearsExperience = yearsExperience
        };

    // ── CalculateRawDfv ────────────────────────────────────────────────────
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
        var wrSim = MakeCareerSim("wr1", "WR", 24, yearOneValue: 150);
        var rbSim = MakeCareerSim("rb1", "RB", 24, yearOneValue: 150);
        var sut = CreateSut();
        var wrDfv = sut.CalculateRawDfv(wrSim, "WR");
        var rbDfv = sut.CalculateRawDfv(rbSim, "RB");
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

    // ── CalculateAllAsync ──────────────────────────────────────────────────
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

        // Bulk-load mock — all sims returned in one call
        _careerRepo
            .Setup(r => r.GetAllBySeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeCareerSim("s1", "WR", 24, yearOneValue: 200),
                MakeCareerSim("s2", "WR", 27, yearOneValue: 150),
                MakeCareerSim("s3", "WR", 31, yearOneValue: 80)
            ]);

        var sut = CreateSut();
        var result = await sut.CalculateAllAsync(2026);
        result.Should().HaveCount(3);
        result.Should().AllSatisfy(v => v.TradeValue.Should().BeInRange(0, 100));
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
            .Setup(r => r.GetAllBySeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeCareerSim("elite", "RB", 23, yearOneValue: 220),
                MakeCareerSim("bench", "RB", 30, yearOneValue: 60)
            ]);

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
            MakeValuation("has-sim", "TE", 25),
            MakeValuation("no-sim", "TE", 26)
        };
        _valuationRepo
            .Setup(r => r.GetByPositionAsync("TE", It.IsAny<CancellationToken>()))
            .ReturnsAsync(valuations);
        _valuationRepo
            .Setup(r => r.GetByPositionAsync(It.Is<string>(p => p != "TE"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Only "has-sim" is in the bulk result — "no-sim" is absent
        _careerRepo
            .Setup(r => r.GetAllBySeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeCareerSim("has-sim", "TE", 25)]);

        var sut = CreateSut();
        var result = await sut.CalculateAllAsync(2026);
        var noSim = result.First(r => r.SleeperPlayerId == "no-sim");
        noSim.TradeValue.Should().Be(0);
    }

    [Fact]
    public async Task CalculateAllAsync_BreakoutBoost_IncreasesTradeValue()
    {
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
            .Setup(r => r.GetAllBySeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeCareerSim("high-bo", "WR", 24, yearOneValue: 150),
                MakeCareerSim("low-bo",  "WR", 24, yearOneValue: 150)
            ]);

        var sut = CreateSut();
        var result = await sut.CalculateAllAsync(2026);
        var highBo = result.First(r => r.SleeperPlayerId == "high-bo");
        var lowBo = result.First(r => r.SleeperPlayerId == "low-bo");
        highBo.TradeValue.Should().BeGreaterThan(lowBo.TradeValue);
    }

    // ── FAN-159: ModelValue — the value the FP blend has not touched ────────

    private static FantasyProsRookieRankingDocument MakeFpDynastyRank(
        string sleeperPlayerId, int fpRank, string position = "WR")
        => new()
        {
            SleeperPlayerId = sleeperPlayerId,
            PlayerName = sleeperPlayerId,
            Position = position,
            FantasyProsRank = fpRank,
            Season = 2026,
            RankingType = "Dynasty"
        };

    /// <summary>
    /// The property FAN-159 turns on, pinned as a test rather than an argument:
    /// when the FantasyPros blend disagrees with the model hard enough to
    /// reverse two players' order, TradeValue follows FantasyPros and ModelValue
    /// follows the model.
    ///
    /// <para>
    /// Without this, the calibration harness ranks by a value that is 65%
    /// FantasyPros rank and scores it against FantasyPros rank — it grades the
    /// anchor against itself, and a model producing noise still posts a high ρ.
    /// If someone later "simplifies" ModelValue into an alias for TradeValue,
    /// every calibration number silently goes back to being self-referential and
    /// nothing else in the suite would notice. This test is the thing that
    /// notices.
    /// </para>
    /// </summary>
    [Fact]
    public async Task CalculateAllAsync_WhenFpBlendReversesTheModelsOrder_ModelValueKeepsTheModelsOrder()
    {
        // "a" is the model's clear favourite; "b" is FantasyPros'. The gap in
        // both directions is deliberately wide enough that the outcome does not
        // depend on the exact blend weight or normalisation exponent — only on
        // whether the FP anchor is applied at all.
        var valuations = new List<DynastyValuationDocument>
        {
            MakeValuation("a", "WR", 24),
            MakeValuation("b", "WR", 24),
            MakeValuation("c", "WR", 29)
        };
        _valuationRepo
            .Setup(r => r.GetByPositionAsync("WR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(valuations);
        _valuationRepo
            .Setup(r => r.GetByPositionAsync(It.Is<string>(p => p != "WR"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _careerRepo
            .Setup(r => r.GetAllBySeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeCareerSim("a", "WR", 24, yearOneValue: 240),
                MakeCareerSim("b", "WR", 24, yearOneValue: 120),
                MakeCareerSim("c", "WR", 29, yearOneValue: 60)
            ]);

        // FantasyPros takes the opposite view: b is a top-5 dynasty asset,
        // a is barely rostered.
        _fpRookieRepo
            .Setup(r => r.GetAllBySeasonAndTypeAsync(It.IsAny<int>(), "Dynasty", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeFpDynastyRank("b", fpRank: 1),
                MakeFpDynastyRank("a", fpRank: 300)
            ]);

        var sut = CreateSut();
        var result = await sut.CalculateAllAsync(2026);

        var a = result.First(r => r.SleeperPlayerId == "a");
        var b = result.First(r => r.SleeperPlayerId == "b");

        // What the site serves: FantasyPros wins, as intended — the blend is a
        // deliberate product decision and is not what this ticket changes.
        b.TradeValue.Should().BeGreaterThan(a.TradeValue,
            "the FP blend is supposed to move TradeValue toward consensus");

        // What the harness measures: the model's own ordering, intact.
        a.ModelValue.Should().BeGreaterThan(b.ModelValue,
            "ModelValue must not carry the FantasyPros anchor, or calibration is " +
            "grading FantasyPros against itself");
    }

    /// <summary>
    /// ModelValue has to actually reach the document. A field that is computed
    /// and then dropped is the shape of FAN-138/140/141 — the pipeline reports
    /// success and the harness reads zeros.
    /// </summary>
    [Fact]
    public async Task CalculateAllAsync_StampsModelValue_OnEveryScoredPlayer()
    {
        var valuations = new List<DynastyValuationDocument>
        {
            MakeValuation("top", "WR", 24),
            MakeValuation("mid", "WR", 26),
            MakeValuation("low", "WR", 30)
        };
        _valuationRepo
            .Setup(r => r.GetByPositionAsync("WR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(valuations);
        _valuationRepo
            .Setup(r => r.GetByPositionAsync(It.Is<string>(p => p != "WR"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _careerRepo
            .Setup(r => r.GetAllBySeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeCareerSim("top", "WR", 24, yearOneValue: 220),
                MakeCareerSim("mid", "WR", 26, yearOneValue: 140),
                MakeCareerSim("low", "WR", 30, yearOneValue: 70)
            ]);

        var sut = CreateSut();
        var result = await sut.CalculateAllAsync(2026);

        result.First(r => r.SleeperPlayerId == "top").ModelValue.Should().BeGreaterThan(0);
        result.Should().AllSatisfy(v => v.ModelValue.Should().BeInRange(0, 100));

        // With no FantasyPros rows at all there is nothing to blend toward, so
        // the two values must agree exactly. If they ever diverge here, a step
        // has been added to one track and not the other.
        result.Should().AllSatisfy(v => v.ModelValue.Should().Be(v.TradeValue));
    }

}
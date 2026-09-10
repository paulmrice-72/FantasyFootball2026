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
    private readonly Mock<IPositionalValueCurveRepository> _curveRepo = new();

    private DfvCalculationService CreateSut()
    {
        _fpRookieRepo
            .Setup(r => r.GetAllBySeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // FAN-153 phase 2. Every fixture below now needs a curve to price
        // against, and the default is a flat zero at every position and year.
        //
        // Deliberate rather than lazy: with a replacement level of zero, value
        // over replacement reduces exactly to the discounted career total these
        // fixtures were written against, so each test keeps measuring the stage
        // it was written to measure — the FP blend, the guardrail caps, the
        // removed FAN-170 gate — instead of quietly becoming a test of the new
        // one. VOR itself is pinned by the tests that supply real curves.
        _curveRepo
            .Setup(r => r.GetAllBySeasonAndFormatAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FlatCurves(level: 0.0));

        return new(
            _careerRepo.Object,
            _valuationRepo.Object,
            _fpRookieRepo.Object,
            _curveRepo.Object,
            NullLogger<DfvCalculationService>.Instance);
    }

    /// <summary>
    /// A curve set carrying the same replacement level at every position and
    /// projected year.
    /// </summary>
    private static List<PositionalValueCurveDocument> FlatCurves(
        double level, int season = 2026, int years = 5, int depth = 120)
        => (from year in Enumerable.Range(season, years)
            from position in new[] { "QB", "RB", "WR", "TE" }
            select new PositionalValueCurveDocument
            {
                Id = $"{season}:HalfPpr:{year}:{position}",
                Season = season,
                ScoringFormat = "HalfPpr",
                Year = year,
                Position = position,
                DescendingSeasonValues = [.. Enumerable.Repeat(level, depth)],
                PoolSize = depth,
                Depth = depth,
                ComputedAt = DateTime.UtcNow
            }).ToList();

    /// <summary>
    /// A curve stepping down by <paramref name="step"/> from
    /// <paramref name="top"/>, so a cutoff at index i reads
    /// <c>top - step * i</c> and a test's arithmetic can be checked by hand.
    /// </summary>
    private static PositionalValueCurveDocument MakeCurve(
        string position, int year, double top, double step, int poolSize, int season = 2026)
    {
        var values = Enumerable.Range(0, poolSize).Select(i => top - step * i).ToList();
        return new PositionalValueCurveDocument
        {
            Season = season,
            ScoringFormat = "Superflex",
            Year = year,
            Position = position,
            Id = $"{season}:Superflex:{year}:{position}",
            DescendingSeasonValues = values,
            PoolSize = poolSize,
            Depth = values.Count,
            ComputedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// A career simulation whose every projected season carries the same value,
    /// so the surplus over replacement is the same number every year and the
    /// discounted sum is that number times a factor a reader can verify.
    /// </summary>
    private static CareerSimulationDocument MakeFlatCareerSim(
        string sleeperPlayerId, string position, double seasonValue,
        int season = 2026, int years = 5)
        => new()
        {
            SleeperPlayerId = sleeperPlayerId,
            PlayerName = sleeperPlayerId,
            Position = position,
            CurrentAge = 26,
            Season = season,
            Iterations = 1000,
            YearProjections = [.. Enumerable.Range(0, years).Select(i => new CareerYearProjection
            {
                Year = season + i,
                AgeAtYear = 26 + i,
                SeasonValue = seasonValue,
                MedianFppg = seasonValue / 17.0,
                AgingMultiplier = 1.0,
                InjuryRisk = 0.0,
                ExpectedGamesPlayed = 17,
                Phase = CareerPhase.Prime
            })]
        };

    private static CareerSimulationDocument MakeCareerSim(
        string sleeperPlayerId, string position, int currentAge, double yearOneValue = 150.0,
        double medianFppg = 12.0)
    {
        var projections = new List<CareerYearProjection>();
        for (int i = 0; i < 5; i++)
        {
            projections.Add(new CareerYearProjection
            {
                Year = 2026 + i,
                AgeAtYear = currentAge + i,
                SeasonValue = yearOneValue * Math.Pow(0.9, i),
                MedianFppg = medianFppg,
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

    // ── FAN-170: the year 0-1 depth gate, and why it is gone ───────────────

    /// <summary>
    /// The removed gate zeroed any non-QB with <= 1 year of experience, no FP
    /// rookie rank, and no projected season clearing StarterThresholdDfv —
    /// 9.0 for a TE, which was the same number as the TE prior the projection
    /// was generated from. A player it caught was not ranked low, he was
    /// removed: NormalizeAcrossAllPositions only ranks raw > 0, so he left the
    /// P2 population entirely (measured 2026-09-08: Elijah Arroyo at RV 0,
    /// TE 91 of 92, against an FP rank of 243).
    ///
    /// This pins the replacement behaviour: a weak projection is a low rank,
    /// not a deletion. All three TEs here project below the old 9.0 threshold,
    /// and the year-1 player's projection sits between the other two — he must
    /// come back with a real value, ranked where his simulation puts him.
    ///
    /// The fixture needs a third player below him on purpose. P2 normalization
    /// scores the last-ranked player `ceiling * (1 - 1)^exponent`, which is
    /// exactly 0 — so with only two players the weaker one reads 0 whatever
    /// this method does, and the test would be measuring the normalizer's
    /// bottom rank rather than the gate. Worth knowing more generally: a
    /// RawValue of 0 is ambiguous at the very bottom of the board.
    /// </summary>
    [Fact]
    public async Task CalculateAllAsync_YearOneUnrankedPlayerBelowTheOldStarterThreshold_IsRankedNotZeroed()
    {
        var valuations = new List<DynastyValuationDocument>
        {
            MakeValuation("veteran",        "TE", 28, yearsExperience: 6),
            MakeValuation("year-one-depth", "TE", 23, yearsExperience: 1),
            MakeValuation("tail",           "TE", 31, yearsExperience: 9)
        };
        _valuationRepo
            .Setup(r => r.GetByPositionAsync("TE", It.IsAny<CancellationToken>()))
            .ReturnsAsync(valuations);
        _valuationRepo
            .Setup(r => r.GetByPositionAsync(It.Is<string>(p => p != "TE"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // Every one of these projects under the old TE threshold of 9.0 in
        // every year — the two veterans only escaped the gate because of their
        // experience, not their numbers.
        _careerRepo
            .Setup(r => r.GetAllBySeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeCareerSim("veteran",        "TE", 28, yearOneValue: 140, medianFppg: 8.0),
                MakeCareerSim("year-one-depth", "TE", 23, yearOneValue: 90,  medianFppg: 4.0),
                MakeCareerSim("tail",           "TE", 31, yearOneValue: 30,  medianFppg: 3.0)
            ]);

        var sut = CreateSut();
        var result = await sut.CalculateAllAsync(2026);

        var yearOne = result.First(r => r.SleeperPlayerId == "year-one-depth");
        var veteran = result.First(r => r.SleeperPlayerId == "veteran");

        yearOne.RawValue.Should().BeGreaterThan(0,
            "a projection below a threshold is a reason to rank a player low, not to " +
            "remove him from the board — and the threshold it failed was equal to the " +
            "prior that generated the projection");
        veteran.RawValue.Should().BeGreaterThan(yearOne.RawValue,
            "the stronger career simulation must still outrank the weaker one");
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

    // ── FAN-166: RawValue — the value before the positional guardrail caps ──

    /// <summary>
    /// RawValue has to reach the document, and the guardrails must only ever
    /// lower a value. The second half is the invariant that makes
    /// <c>RawValue - ModelValue</c> readable as "what the caps cost": if a
    /// guardrail could ever raise a player, the difference would be a mixture of
    /// two effects with opposite signs and the decomposition would mean nothing.
    /// </summary>
    [Fact]
    public async Task CalculateAllAsync_StampsRawValue_AndGuardrailsOnlyEverLowerIt()
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

        result.First(r => r.SleeperPlayerId == "top").RawValue.Should().BeGreaterThan(0);
        result.Should().AllSatisfy(v => v.RawValue.Should().BeInRange(0, 100));
        result.Should().AllSatisfy(v => v.ModelValue.Should().BeLessThanOrEqualTo(v.RawValue,
            "the positional guardrails are ceilings — a value that rose through one " +
            "would make RawValue - ModelValue unreadable as the caps' contribution"));
    }

    /// <summary>
    /// The defect FAN-166 was raised for, pinned as a test so a fix has a target
    /// and a regression has a tripwire.
    ///
    /// <para>
    /// Measured in dev on 2026-09-08: sorted by raw DFV, A.J. Brown is 428 and
    /// Jeremy Ruckert is 195; sorted by ModelValue, Ruckert is ahead.
    /// <c>NormalizeAcrossAllPositions</c> is a single global sort and cannot
    /// invert an ordering, so the inversion is entirely
    /// <c>ApplyPositionalGuardrails</c> — the only stage that partitions by
    /// position. The cap tables were fitted against the blended distribution, in
    /// which the FantasyPros blend had already corrected each player's positional
    /// rank; applied to the model's own uncorrected order they bind somewhere
    /// else entirely.
    /// </para>
    ///
    /// <para>
    /// The fixture reproduces the shape rather than the players: a receiver who
    /// is strong on the whole board but only ~50th at his own position (cap 35),
    /// against a tight end who is far weaker on the board but ~10th at his
    /// position (cap 70). A deep filler pool puts both high enough on the P2
    /// curve that the caps actually bind, which is what production looks like at
    /// N≈600 and what a small fixture would miss.
    /// </para>
    ///
    /// <para>
    /// This test asserts the current, wrong behaviour on ModelValue deliberately.
    /// When the cross-position basis is fixed — replacement level per FAN-153, or
    /// the deep cap tiers removed — this test should FAIL, and the correct
    /// response is to invert the ModelValue assertion, not to delete it.
    /// </para>
    ///
    /// <para>
    /// FAN-153 phase 2 note: it still passes, and that is the fixture's doing
    /// rather than a claim that nothing changed. The default curves are flat at
    /// zero, so this run has no replacement level to subtract and the inversion
    /// it pins survives untouched. What decides whether the real defect is gone
    /// is the calibration run against real curves, not this test — and when the
    /// cap tables come out in their own commit, this is the assertion to invert.
    /// </para>
    /// </summary>
    [Fact]
    public async Task CalculateAllAsync_GuardrailCaps_InvertTheModelsCrossPositionOrdering()
    {
        // 60 receivers, descending. Our subject is the 50th — comfortably inside
        // the top tenth of the whole board, and just past the 45/46 cliff in
        // GetWrGuardrailCap where the ceiling drops from 50 to 35.
        var wrs = Enumerable.Range(0, 60)
            .Select(i => (Id: $"wr{i + 1}", Value: 300.0 - i * 4)).ToList();

        // 12 tight ends, all far weaker in raw terms than any of those receivers.
        // Our subject is the 10th, which sits in GetTeGuardrailCap's rank <=12
        // band — a ceiling of 70.
        var tes = Enumerable.Range(0, 12)
            .Select(i => (Id: $"te{i + 1}", Value: 60.0 - i)).ToList();

        // Filler below both groups so the pool is production-sized. Without it
        // the P2 curve collapses and neither cap binds, which would make the
        // test pass for the wrong reason.
        var filler = Enumerable.Range(0, 500)
            .Select(i => (Id: $"rb{i + 1}", Value: 10.0 - i * 0.01)).ToList();

        _valuationRepo
            .Setup(r => r.GetByPositionAsync("WR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(wrs.Select(w => MakeValuation(w.Id, "WR", 26)).ToList());
        _valuationRepo
            .Setup(r => r.GetByPositionAsync("TE", It.IsAny<CancellationToken>()))
            .ReturnsAsync(tes.Select(t => MakeValuation(t.Id, "TE", 26)).ToList());
        _valuationRepo
            .Setup(r => r.GetByPositionAsync("RB", It.IsAny<CancellationToken>()))
            .ReturnsAsync(filler.Select(f => MakeValuation(f.Id, "RB", 26)).ToList());
        _valuationRepo
            .Setup(r => r.GetByPositionAsync("QB", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _careerRepo
            .Setup(r => r.GetAllBySeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                .. wrs.Select(w => MakeCareerSim(w.Id, "WR", 26, w.Value)),
                .. tes.Select(t => MakeCareerSim(t.Id, "TE", 26, t.Value)),
                .. filler.Select(f => MakeCareerSim(f.Id, "RB", 26, f.Value))
            ]);

        var sut = CreateSut();
        var result = await sut.CalculateAllAsync(2026);

        var wr = result.First(r => r.SleeperPlayerId == "wr50");
        var te = result.First(r => r.SleeperPlayerId == "te10");

        // What the model produced. The receiver's five-year discounted value is
        // roughly double the tight end's, and the normalization preserved it.
        wr.RawValue.Should().BeGreaterThan(te.RawValue,
            "a global sort cannot invert an ordering — if this fails, the defect " +
            "is upstream of the guardrails and this test is pointed at the wrong stage");

        // What gets stamped. Current behaviour, and the thing FAN-166 is about:
        // the cap tables reverse the model across positions.
        te.ModelValue.Should().BeGreaterThan(wr.ModelValue,
            "the guardrail cap tables currently invert the model's cross-position " +
            "ordering — when this is fixed, invert this assertion rather than removing it");
    }

    // ── FAN-153 phase 2: value over replacement ────────────────────────────

    /// <summary>
    /// The acceptance criterion this ticket was written around: <i>a 1-QB league
    /// and a superflex league produce different QB orderings from the same
    /// projections, with a test that asserts it.</i>
    ///
    /// <para>
    /// Nothing about the players changes between the two runs — same simulations,
    /// same curves, same everything. The only difference is that a SUPER_FLEX slot
    /// makes quarterbacks eligible for a flex, so they win those slots on
    /// projection and the QB cutoff moves a full round deeper. Read at index 12 the
    /// QB baseline is 252; read at index 24 it is 204.
    /// </para>
    ///
    /// <para>
    /// The fixture is arithmetic a reader can check. Every projected season carries
    /// the same value and every curve steps by a constant, so the quarterback's
    /// surplus is 245 − 252 = −7 per season in a 1-QB league and 245 − 204 = +41 in
    /// superflex, while the receiver's is 200 − 164 = +36 in both. Discounted over
    /// five seasons at their position rates (QB 0.10, WR 0.12) that is −29 against
    /// +145 one way and +171 against +145 the other — the order reverses, and it
    /// reverses because of the league shape rather than because of anything the
    /// model believes about either player.
    /// </para>
    ///
    /// <para>
    /// RawValue is the assertion target on purpose: it is snapshotted before the
    /// positional guardrail caps, so this measures value over replacement alone.
    /// </para>
    /// </summary>
    [Fact]
    public async Task CalculateAllAsync_SameProjections_OneQbAndSuperflexLeaguesOrderQuarterbacksDifferently()
    {
        // 40 quarterbacks: 300 at the top, 4 apart. Index 12 → 252, index 24 → 204.
        // 80 receivers: 200 at the top, 1 apart. Index 36 → 164.
        var curves = Enumerable.Range(2026, 5)
            .SelectMany(year => new[]
            {
                MakeCurve("QB", year, top: 300, step: 4, poolSize: 40),
                MakeCurve("WR", year, top: 200, step: 1, poolSize: 80)
            })
            .ToList();

        _valuationRepo
            .Setup(r => r.GetByPositionAsync("QB", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeValuation("qb-mid", "QB", 26)]);
        _valuationRepo
            .Setup(r => r.GetByPositionAsync("WR", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeValuation("wr-high", "WR", 26),
                MakeValuation("wr-mid",  "WR", 26),
                MakeValuation("wr-low",  "WR", 26),
                MakeValuation("wr-tail", "WR", 26)
            ]);
        _valuationRepo
            .Setup(r => r.GetByPositionAsync(It.Is<string>(p => p == "RB" || p == "TE"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _careerRepo
            .Setup(r => r.GetAllBySeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeFlatCareerSim("qb-mid",  "QB", 245),
                MakeFlatCareerSim("wr-high", "WR", 260),
                MakeFlatCareerSim("wr-mid",  "WR", 200),
                MakeFlatCareerSim("wr-low",  "WR", 170),
                MakeFlatCareerSim("wr-tail", "WR", 100)
            ]);

        var sut = CreateSut();

        // Registered after CreateSut so it wins over the flat-zero default, and
        // keyed on "HalfPpr" exactly rather than any string. Both runs below have
        // to find these — the superflex run included — or the roster shape is
        // being used as a storage key for a distribution that does not vary with
        // it, which is how the first phase 2 run failed: curves built under
        // "Superflex", a DFV run defaulting to "HalfPpr", nothing found.
        _curveRepo
            .Setup(r => r.GetAllBySeasonAndFormatAsync(
                It.IsAny<int>(), "HalfPpr", It.IsAny<CancellationToken>()))
            .ReturnsAsync(curves);

        var oneQb = await sut.CalculateAllAsync(2026, ScoringFormat.HalfPpr);
        var oneQbQuarterback = oneQb.First(v => v.SleeperPlayerId == "qb-mid").RawValue;
        var oneQbReceiver = oneQb.First(v => v.SleeperPlayerId == "wr-mid").RawValue;

        var superflex = await sut.CalculateAllAsync(2026, ScoringFormat.Superflex);
        var superflexQuarterback = superflex.First(v => v.SleeperPlayerId == "qb-mid").RawValue;
        var superflexReceiver = superflex.First(v => v.SleeperPlayerId == "wr-mid").RawValue;

        oneQbReceiver.Should().BeGreaterThan(oneQbQuarterback,
            "in a 1-QB league the quarterback projects below replacement and the " +
            "receiver comfortably above it");

        superflexQuarterback.Should().BeGreaterThan(superflexReceiver,
            "the same quarterback clears a superflex baseline by 41 points a season — " +
            "if this fails, the roster configuration is not reaching the replacement " +
            "level and the board is priced for one league shape whatever format it is run in");
    }

    /// <summary>
    /// The trap the phase 1 close-out flagged: <c>NormalizeAcrossAllPositions</c>
    /// used to rank only players with a value above zero, and value over
    /// replacement is signed. Shipping against that filter would have deleted most
    /// of the board — FAN-170's defect (a low projection read as an absence) at a
    /// few hundred times the scale.
    ///
    /// <para>
    /// Both halves matter and they are asserted together. A player projected below
    /// replacement is <i>bad</i> and must still hold a rank; a player with no
    /// career simulation is <i>absent</i> and must still read zero. If those two
    /// ever collapse into each other again, this is the test that notices.
    /// </para>
    /// </summary>
    [Fact]
    public async Task CalculateAllAsync_SubReplacementPlayer_IsRankedNotZeroed()
    {
        _valuationRepo
            .Setup(r => r.GetByPositionAsync("WR", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeValuation("starter",         "WR", 25),
                MakeValuation("sub-replacement", "WR", 27),
                MakeValuation("deep",            "WR", 30),
                MakeValuation("no-sim",          "WR", 24)
            ]);
        _valuationRepo
            .Setup(r => r.GetByPositionAsync(It.Is<string>(p => p != "WR"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // "no-sim" is deliberately absent from the bulk result.
        _careerRepo
            .Setup(r => r.GetAllBySeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeFlatCareerSim("starter",         "WR", 250),
                MakeFlatCareerSim("sub-replacement", "WR", 100),
                MakeFlatCareerSim("deep",            "WR", 50)
            ]);

        var sut = CreateSut();

        // A flat baseline of 150 a season, registered after CreateSut so it wins
        // over the flat-zero default — "below replacement" is then a plain
        // arithmetic fact about the fixture rather than an emergent one.
        _curveRepo
            .Setup(r => r.GetAllBySeasonAndFormatAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FlatCurves(level: 150.0));

        var result = await sut.CalculateAllAsync(2026);

        var starter = result.First(v => v.SleeperPlayerId == "starter");
        var subReplacement = result.First(v => v.SleeperPlayerId == "sub-replacement");
        var noSim = result.First(v => v.SleeperPlayerId == "no-sim");

        subReplacement.RawValue.Should().BeGreaterThan(0,
            "a player projected below replacement is ranked low, not removed — the " +
            "normalization population is the set of scored players, not the set of " +
            "positive ones");
        starter.RawValue.Should().BeGreaterThan(subReplacement.RawValue,
            "and the ordering between them is still the model's");
        noSim.RawValue.Should().Be(0,
            "no career simulation is a genuine absence, which is the only thing that " +
            "keeps a player out of the scored set");
    }

    /// <summary>
    /// Without curves there is no replacement level, and valuing players on raw
    /// point totals is the cross-position defect this ticket exists to remove. A
    /// run in that state fails loudly rather than quietly reverting to the old
    /// behaviour — a silent fallback would look like a model change on the next
    /// calibration run and cost a session to find.
    ///
    /// <para>
    /// The curves are resolved before the career simulations are bulk-loaded, so
    /// this throws on the thing that is actually missing rather than several
    /// thousand documents later. The simulation stub below exists only to keep
    /// that true if the ordering is ever changed back: without it the run would
    /// fail on a null bulk-load and this test would report the wrong defect.
    /// </para>
    /// </summary>
    [Fact]
    public async Task CalculateAllAsync_NoValueCurves_ThrowsRatherThanFallingBack()
    {
        _valuationRepo
            .Setup(r => r.GetByPositionAsync("WR", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeValuation("s1", "WR", 25)]);
        _valuationRepo
            .Setup(r => r.GetByPositionAsync(It.Is<string>(p => p != "WR"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _careerRepo
            .Setup(r => r.GetAllBySeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeFlatCareerSim("s1", "WR", 200)]);

        var sut = CreateSut();

        _curveRepo
            .Setup(r => r.GetAllBySeasonAndFormatAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var act = () => sut.CalculateAllAsync(2026);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*build-value-curves*");
    }

    // ── FAN-153 control experiment (disableVor) ────────────────────────────

    /// <summary>
    /// A curve set carrying a different flat replacement level per position, so
    /// the subtraction is a different constant at each one and the two runs
    /// genuinely disagree about cross-position ordering. Without this the control
    /// and the live path would agree everywhere and the test below would pass for
    /// the wrong reason.
    /// </summary>
    private static List<PositionalValueCurveDocument> PerPositionFlatCurves(
        IReadOnlyDictionary<string, double> levelsByPosition,
        int season = 2026, int years = 5, int depth = 120)
        => (from year in Enumerable.Range(season, years)
            from position in new[] { "QB", "RB", "WR", "TE" }
            select new PositionalValueCurveDocument
            {
                Id = $"{season}:HalfPpr:{year}:{position}",
                Season = season,
                ScoringFormat = "HalfPpr",
                Year = year,
                Position = position,
                DescendingSeasonValues =
                    [.. Enumerable.Repeat(levelsByPosition.GetValueOrDefault(position, 0.0), depth)],
                PoolSize = depth,
                Depth = depth,
                ComputedAt = DateTime.UtcNow
            }).ToList();

    /// <summary>
    /// The property the whole control experiment rests on, asserted rather than
    /// argued: the replacement subtraction is a per-position constant and P2
    /// normalization is rank-based, so turning the subtraction off can reorder
    /// players <i>across</i> positions but never <i>within</i> one.
    ///
    /// <para>
    /// If this ever fails, the control is not a control and no rho comparison
    /// built on it means anything — which is the specific way FAN-175 could be
    /// mis-diagnosed a second time.
    /// </para>
    /// </summary>
    [Fact]
    public async Task CalculateAllAsync_ControlRun_PreservesWithinPositionOrdering_ButNotCrossPosition()
    {
        _valuationRepo
            .Setup(r => r.GetByPositionAsync("QB", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeValuation("qb-elite", "QB", 25),
                MakeValuation("qb-mid",   "QB", 27),
                MakeValuation("qb-deep",  "QB", 30)
            ]);
        _valuationRepo
            .Setup(r => r.GetByPositionAsync("WR", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeValuation("wr-elite", "WR", 24),
                MakeValuation("wr-mid",   "WR", 26),
                MakeValuation("wr-deep",  "WR", 29)
            ]);
        _valuationRepo
            .Setup(r => r.GetByPositionAsync(
                It.Is<string>(p => p != "QB" && p != "WR"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _careerRepo
            .Setup(r => r.GetAllBySeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeFlatCareerSim("qb-elite", "QB", 400),
                MakeFlatCareerSim("qb-mid",   "QB", 300),
                MakeFlatCareerSim("qb-deep",  "QB", 220),
                MakeFlatCareerSim("wr-elite", "WR", 380),
                MakeFlatCareerSim("wr-mid",   "WR", 290),
                MakeFlatCareerSim("wr-deep",  "WR", 210)
            ]);

        var sut = CreateSut();

        // Registered after CreateSut so it wins over the flat-zero default.
        // QB's replacement level is far above WR's, which is what makes the
        // cross-position half of this assertion non-trivial.
        _curveRepo
            .Setup(r => r.GetAllBySeasonAndFormatAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PerPositionFlatCurves(new Dictionary<string, double>
            {
                ["QB"] = 250.0,
                ["WR"] = 40.0
            }));

        // Snapshotted immediately: both runs mutate the same valuation documents,
        // so the second overwrites the first's stamped values.
        static List<string> OrderWithin(List<DynastyValuationDocument> result, string position)
            => result
                .Where(v => v.Position == position)
                .OrderByDescending(v => v.RawValue)
                .Select(v => v.SleeperPlayerId)
                .ToList();

        static List<string> OrderOverall(List<DynastyValuationDocument> result)
            => result
                .OrderByDescending(v => v.RawValue)
                .Select(v => v.SleeperPlayerId)
                .ToList();

        var vorResult = await sut.CalculateAllAsync(2026, ScoringFormat.HalfPpr);
        var vorQb = OrderWithin(vorResult, "QB");
        var vorWr = OrderWithin(vorResult, "WR");
        var vorOverall = OrderOverall(vorResult);

        var controlResult = await sut.CalculateAllAsync(2026, ScoringFormat.HalfPpr, disableVor: true);
        var controlQb = OrderWithin(controlResult, "QB");
        var controlWr = OrderWithin(controlResult, "WR");
        var controlOverall = OrderOverall(controlResult);

        controlQb.Should().Equal(vorQb,
            "the replacement subtraction is a per-position constant, so it cannot " +
            "reorder quarterbacks against each other");
        controlWr.Should().Equal(vorWr,
            "and the same holds at every other position");

        controlOverall.Should().NotEqual(vorOverall,
            "while the cross-position ladder is exactly what the subtraction exists " +
            "to change — if this passes too, the fixture's replacement levels are not " +
            "separated enough for the test above to have proved anything");
    }

    /// <summary>
    /// The control path must not resolve curves at all. It scores today's
    /// simulations without the subtraction, so a missing curve is not an error
    /// on this path — and a control run that failed on the state it was written
    /// to measure would be useless.
    /// </summary>
    [Fact]
    public async Task CalculateAllAsync_ControlRun_ScoresWithoutValueCurves()
    {
        _valuationRepo
            .Setup(r => r.GetByPositionAsync("WR", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeValuation("s1", "WR", 25), MakeValuation("s2", "WR", 27)]);
        _valuationRepo
            .Setup(r => r.GetByPositionAsync(It.Is<string>(p => p != "WR"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _careerRepo
            .Setup(r => r.GetAllBySeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeFlatCareerSim("s1", "WR", 300),
                MakeFlatCareerSim("s2", "WR", 120)
            ]);

        var sut = CreateSut();

        _curveRepo
            .Setup(r => r.GetAllBySeasonAndFormatAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await sut.CalculateAllAsync(2026, ScoringFormat.HalfPpr, disableVor: true);

        result.Should().HaveCount(2);
        result.First(v => v.SleeperPlayerId == "s1").RawValue
            .Should().BeGreaterThan(result.First(v => v.SleeperPlayerId == "s2").RawValue);

        _curveRepo.Verify(
            r => r.GetAllBySeasonAndFormatAsync(
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the control path has no replacement level to resolve, so it should not " +
            "read the curve repository at all");
    }

    // ── FAN-175 ────────────────────────────────────────────────────────────

    /// <summary>
    /// The reason <c>IsScored</c> is a flag and not <c>RawValue &gt; 0</c>.
    ///
    /// <para>
    /// P2 normalization assigns the last-ranked scored player exactly <c>0.00</c> —
    /// the same number an unscored player carries — so a magnitude test drops one
    /// real player from every population it selects, silently, forever. This test
    /// pins the distinction at the only place it is observable: a fixture whose
    /// worst scored player and whose zeroed player both stamp 0.0, and where only
    /// one of them is scored.
    /// </para>
    /// </summary>
    [Fact]
    public async Task CalculateAllAsync_LastRankedScoredPlayer_StampsZeroButIsScored()
    {
        _valuationRepo
            .Setup(r => r.GetByPositionAsync("WR", It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeValuation("best",   "WR", 25),
                MakeValuation("middle", "WR", 27),
                MakeValuation("worst",  "WR", 30),
                MakeValuation("no-sim", "WR", 24)
            ]);
        _valuationRepo
            .Setup(r => r.GetByPositionAsync(It.Is<string>(p => p != "WR"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // "no-sim" is absent from the bulk result — a genuine absence, and the only
        // thing that should keep a player out of the scored set.
        _careerRepo
            .Setup(r => r.GetAllBySeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeFlatCareerSim("best",   "WR", 300),
                MakeFlatCareerSim("middle", "WR", 200),
                MakeFlatCareerSim("worst",  "WR", 100)
            ]);

        var sut = CreateSut();

        var result = await sut.CalculateAllAsync(2026);

        var worst = result.First(v => v.SleeperPlayerId == "worst");
        var noSim = result.First(v => v.SleeperPlayerId == "no-sim");

        worst.RawValue.Should().Be(0.0,
            "the last-ranked scored player normalizes to exactly the ceiling times " +
            "zero — this is the collision that makes a magnitude test unusable");
        noSim.RawValue.Should().Be(0.0, "and an unscored player carries the same number");

        worst.IsScored.Should().BeTrue(
            "he was valued and ranked last, which is a result, not an absence");
        noSim.IsScored.Should().BeFalse(
            "he was never valued at all — which is what every selection query now " +
            "filters on, instead of guessing from the value");

        result.Where(v => v.IsScored).Should().HaveCount(3);
    }

    /// <summary>
    /// A stale flag is the one value this field must never carry, because it is what
    /// the selection queries trust. A player the run skips is stamped false rather
    /// than left holding whatever a previous run wrote.
    /// </summary>
    [Fact]
    public async Task CalculateAllAsync_PlayerZeroedThisRun_ClearsAStaleScoredFlag()
    {
        var previouslyScored = MakeValuation("was-scored", "WR", 28);
        previouslyScored.IsScored = true;
        previouslyScored.RawValue = 84.2;

        _valuationRepo
            .Setup(r => r.GetByPositionAsync("WR", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeValuation("still-scored", "WR", 25), previouslyScored]);
        _valuationRepo
            .Setup(r => r.GetByPositionAsync(It.Is<string>(p => p != "WR"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // This run has no simulation for him, so this run did not value him.
        _careerRepo
            .Setup(r => r.GetAllBySeasonAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeFlatCareerSim("still-scored", "WR", 300)]);

        var sut = CreateSut();

        var result = await sut.CalculateAllAsync(2026);

        result.First(v => v.SleeperPlayerId == "was-scored").IsScored
            .Should().BeFalse("the flag describes this run, not the last one");
        result.First(v => v.SleeperPlayerId == "still-scored").IsScored
            .Should().BeTrue();
    }
}
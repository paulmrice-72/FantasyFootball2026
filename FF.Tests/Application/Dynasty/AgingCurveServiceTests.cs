using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Domain.Entities;
using FF.Domain.Enums;
using FF.Domain.Services;
using FF.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FF.Tests.Application.Dynasty;

public class AgingCurveServiceTests
{
    private readonly Mock<IPlayerGameLogRepository> _gameLogRepo = new();
    private readonly Mock<IPlayerRepository> _playerRepo = new();
    private readonly Mock<IAgingCurveRepository> _curveRepo = new();

    /// <summary>
    /// Defaults to a trusted estimator so these tests exercise the fit itself.
    /// Production wires it false (FAN-157) — see
    /// <see cref="BuildAllCurvesAsync_DiscardsTheFittedCurve_WhenTheEstimatorIsNotTrusted"/>
    /// for the gate, and DependencyInjection for the live value.
    /// </summary>
    private IAgingCurveService CreateSut(bool estimatorTrusted = true) =>
        new AgingCurveService(_gameLogRepo.Object, _playerRepo.Object, _curveRepo.Object,
            NullLogger<AgingCurveService>.Instance, estimatorTrusted);

    private void NoData()
    {
        _gameLogRepo
            .Setup(r => r.GetByPositionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _playerRepo
            .Setup(r => r.GetByPositionAsync(It.IsAny<Position>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

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
    public async Task GetAgeMultiplierAsync_PeakAgeHasHighestMultiplier_WhenNoCurveStored(
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

    /// <summary>
    /// FAN-157. This method used to load the age window and then return the
    /// hardcoded default unconditionally — the stored curve was never read. So
    /// every caller of IAgingCurveService got one aging model while
    /// CareerSimulationService used a different one, and nothing in the codebase
    /// pointed out that the two disagreed.
    /// </summary>
    [Fact]
    public async Task GetAgeMultiplierAsync_ReadsTheStoredCurve_NotTheFallback()
    {
        _curveRepo
            .Setup(r => r.GetByPositionAsync("WR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgingCurveDocument
            {
                Position = "WR",
                IsDefaultCurve = false,
                MinAge = 21,
                MaxAge = 35,
                AgeValueMap = new Dictionary<int, double> { [26] = 42.0 }
            });

        var sut = CreateSut();
        var multiplier = await sut.GetAgeMultiplierAsync("WR", 26);

        // 0.42 can only have come from the stored curve; the fallback returns
        // 1.0 at a position's peak age.
        multiplier.Should().BeApproximately(0.42, 0.001);
    }

    [Fact]
    public async Task GetAgeMultiplierAsync_FallsBack_WhenStoredCurveIsTheDefault()
    {
        _curveRepo
            .Setup(r => r.GetByPositionAsync("WR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgingCurveDocument
            {
                Position = "WR",
                IsDefaultCurve = true,
                AgeValueMap = new Dictionary<int, double> { [26] = 42.0 }
            });

        var sut = CreateSut();
        var multiplier = await sut.GetAgeMultiplierAsync("WR", 26);

        multiplier.Should().BeApproximately(1.0, 0.01);
    }

    // ── BuildAllCurvesAsync — default curve fallback ──────────────────────

    [Fact]
    public async Task BuildAllCurvesAsync_ReturnsDefaultCurves_WhenInsufficientData()
    {
        NoData();

        var sut = CreateSut();
        var curves = await sut.BuildAllCurvesAsync();

        curves.Should().HaveCount(4);
        curves.Should().AllSatisfy(c => c.IsDefaultCurve.Should().BeTrue());
        curves.Select(c => c.Position).Should().BeEquivalentTo(["QB", "RB", "WR", "TE"]);
    }

    [Fact]
    public async Task BuildAllCurvesAsync_DefaultCurves_HaveValidAgeValueMaps()
    {
        NoData();

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
        NoData();

        var sut = CreateSut();
        var curves = await sut.BuildAllCurvesAsync();

        var rb = curves.First(c => c.Position == "RB");
        var qb = curves.First(c => c.Position == "QB");

        rb.PeakAge.Should().BeLessThan(qb.PeakAge);
    }

    // ── The FAN-157 defect itself ─────────────────────────────────────────

    /// <summary>
    /// The regression test for this ticket, built as a discrimination rather
    /// than an assertion about output shape.
    /// </summary>
    /// <remarks>
    /// The dataset below has a known true aging curve peaking at 26, and a
    /// survivorship pattern layered on top: weak players leave the league after
    /// 27, strong ones play to 33. That is the real-world structure that broke
    /// the old estimator — pooling every game log by age and averaging measures
    /// <em>who is still playing at each age</em>, and the survivors are the good
    /// ones, so the average climbs with age no matter what aging does.
    ///
    /// <para>
    /// The test asserts both halves. First, that the cross-sectional average
    /// really does rise past the true peak in this data — proving the fixture
    /// contains the trap and the test would have failed before the fix.
    /// Second, that the longitudinal estimator recovers a peak near 26 anyway.
    /// A test that only checked the second half would pass on data where the
    /// old method also worked, and would not be evidence of anything.
    /// </para>
    /// </remarks>
    [Fact]
    public async Task BuildAllCurvesAsync_RecoversTheTruePeak_EvenWhenSurvivorshipRisesWithAge()
    {
        var (logs, players) = BuildSurvivorshipDataset();

        _gameLogRepo
            .Setup(r => r.GetByPositionAsync("WR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(logs);
        _gameLogRepo
            .Setup(r => r.GetByPositionAsync(It.Is<string>(s => s != "WR"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _playerRepo
            .Setup(r => r.GetByPositionAsync(Position.WR, It.IsAny<CancellationToken>()))
            .ReturnsAsync(players);
        _playerRepo
            .Setup(r => r.GetByPositionAsync(It.Is<Position>(p => p != Position.WR), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        // ── Half one: the fixture contains the trap ──────────────────────
        // Pool by age and average, exactly as the old implementation did.
        var refYear = DateTime.UtcNow.Year;
        var ageById = players.ToDictionary(p => p.SleeperPlayerId!, p => p.Age!.Value);
        var crossSectional = logs
            .GroupBy(l => ageById[l.SleeperPlayerId!] - (refYear - l.Season))
            .ToDictionary(g => g.Key, g => g.Average(l => (double)l.FantasyPointsPpr!.Value));

        crossSectional[28].Should().BeGreaterThan(crossSectional[26],
            "the fixture must reproduce the survivorship trap — if the naive average " +
            "does not rise past the true peak here, this test proves nothing");

        // ── Half two: the estimator is not fooled by it ──────────────────
        var sut = CreateSut();
        var curves = await sut.BuildAllCurvesAsync();
        var wr = curves.First(c => c.Position == "WR");

        wr.IsDefaultCurve.Should().BeFalse("the fit should have succeeded on this data");

        wr.PeakAge.Should().BeInRange(25, 27,
            "the true peak is 26 and the longitudinal estimator holds the player " +
            "constant, so composition cannot move it");

        wr.AgeValueMap[30].Should().BeLessThan(wr.AgeValueMap[26],
            "the curve must decline after its peak");
        wr.AgeValueMap[33].Should().BeLessThan(wr.AgeValueMap[30],
            "and keep declining");

        wr.AgeValueMap.Values.Should().AllSatisfy(v => v.Should().BeInRange(0.0, 100.0));
        wr.Coefficients.Should().HaveCount(4);
        wr.SampleSize.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// The gate that production runs behind. On 2026-09-07 the prior-band
    /// tripwire was set at ±2 and rejected QB, WR and TE — but RB fitted a peak
    /// of 23 against a prior of 24, passed on a one-year margin, and went live
    /// alone. One position scored by a known mis-specified estimator while the
    /// other three used the analytic fallback is the worst of the available
    /// outcomes, and a threshold that admits a position by luck is not a safety
    /// property. Hence an explicit gate rather than a tighter number.
    ///
    /// <para>
    /// Note this test uses the same fixture that produces a <em>good</em> fit
    /// (peak 26, well inside the tripwire) — the gate must discard even a curve
    /// that would have passed every other check, or it is not a gate.
    /// </para>
    /// </summary>
    [Fact]
    public async Task BuildAllCurvesAsync_DiscardsTheFittedCurve_WhenTheEstimatorIsNotTrusted()
    {
        var (logs, players) = BuildSurvivorshipDataset();

        _gameLogRepo
            .Setup(r => r.GetByPositionAsync("WR", It.IsAny<CancellationToken>()))
            .ReturnsAsync(logs);
        _gameLogRepo
            .Setup(r => r.GetByPositionAsync(It.Is<string>(s => s != "WR"), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        _playerRepo
            .Setup(r => r.GetByPositionAsync(Position.WR, It.IsAny<CancellationToken>()))
            .ReturnsAsync(players);
        _playerRepo
            .Setup(r => r.GetByPositionAsync(It.Is<Position>(p => p != Position.WR), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var trusted = await CreateSut(estimatorTrusted: true).BuildAllCurvesAsync();
        var gated = await CreateSut(estimatorTrusted: false).BuildAllCurvesAsync();

        // Same data, same fit — the only difference is the gate.
        trusted.First(c => c.Position == "WR").IsDefaultCurve.Should().BeFalse();

        var wr = gated.First(c => c.Position == "WR");
        wr.IsDefaultCurve.Should().BeTrue("a gated run must not return the fitted curve");
        wr.PeakAge.Should().Be(26, "the fallback's own prior, not the fitted peak");
        wr.SampleSize.Should().Be(0);
        wr.Coefficients.Should().BeEmpty();

        gated.Should().AllSatisfy(c => c.IsDefaultCurve.Should().BeTrue(
            "no position may ship a fitted curve while the estimator is gated — " +
            "one position on a different methodology is worse than none"));
    }

    /// <summary>
    /// A monotonically rising age→value series is the exact shape FAN-157 found
    /// stored in production, and nothing objected to it. It must not be
    /// storable silently.
    /// </summary>
    [Fact]
    public void IsPlausibleAgingCurve_RejectsACurveThatRisesWithAge()
    {
        var rising = new Dictionary<int, double>();
        for (var age = 21; age <= 35; age++)
            rising[age] = (age - 20) * 6.0;

        var ok = AgingFallbackCurve.IsPlausibleAgingCurve(rising, 21, 35, out var reason);

        ok.Should().BeFalse();
        reason.Should().Contain("peak age 35");
    }

    [Fact]
    public void IsPlausibleAgingCurve_RejectsATwoHumpedCurve()
    {
        // Rises to 26, falls, then flares back up at the top of the window —
        // the shape a cubic can produce when extrapolated past its own data.
        var twoHumps = new Dictionary<int, double>
        {
            [21] = 60, [22] = 70, [23] = 80, [24] = 90, [25] = 95, [26] = 100,
            [27] = 92, [28] = 84, [29] = 70, [30] = 55, [31] = 40, [32] = 30,
            [33] = 25, [34] = 45, [35] = 65
        };

        var ok = AgingFallbackCurve.IsPlausibleAgingCurve(twoHumps, 21, 35, out var reason);

        ok.Should().BeFalse();
        reason.Should().Contain("not single-peaked");
    }

    [Fact]
    public void IsPlausibleAgingCurve_AcceptsASingleHumpWithFlatTails()
    {
        var singleHump = new Dictionary<int, double>
        {
            [21] = 85, [22] = 85, [23] = 90, [24] = 95, [25] = 98, [26] = 100,
            [27] = 96, [28] = 90, [29] = 82, [30] = 72, [31] = 60, [32] = 48,
            [33] = 40, [34] = 40, [35] = 40
        };

        var ok = AgingFallbackCurve.IsPlausibleAgingCurve(singleHump, 21, 35, out var reason);

        ok.Should().BeTrue(reason);
    }

    // ── Fixture ───────────────────────────────────────────────────────────

    /// <summary>
    /// True aging: a single hump peaking at 26, declining faster after the peak
    /// than it rose before it. Talent-independent by construction, so the
    /// year-over-year ratio between two ages is identical for every player and
    /// the delta estimator can recover it exactly.
    /// </summary>
    private static double TrueAgingFactor(int age)
    {
        var d = age - 26;
        var factor = d <= 0
            ? 1.0 - 0.008 * d * d
            : 1.0 - 0.012 * d * d;
        return Math.Max(0.15, factor);
    }

    private static (List<PlayerGameLogDocument> Logs, List<Player> Players) BuildSurvivorshipDataset()
    {
        // Seasons are generated relative to the current year because ages come
        // from Player.Age (age today) and are converted using the current year.
        // Hardcoding seasons would make this test start failing on 1 January.
        var refYear = DateTime.UtcNow.Year;

        const int PlayerCount = 30;
        const int WeeksPerSeason = 12;   // comfortably above MinGamesPerSeason
        const int DebutAge = 22;

        var logs = new List<PlayerGameLogDocument>();
        var players = new List<Player>();

        for (var i = 0; i < PlayerCount; i++)
        {
            var talent = 6.0 + (i % 10) * 1.5;          // 6.0 … 19.5 FPPG at peak

            // The survivorship rule: only the better players are still in the
            // league in their thirties. This is what makes the age-28+ pool
            // better on average than the age-24 pool for reasons that have
            // nothing to do with aging.
            var exitAge = talent >= 12.0 ? 33 : 27;

            var id = $"sleeper-{i}";
            var currentAge = exitAge + 1;

            var player = Player.Create($"Player{i}", "Test", Position.WR, sleeperPlayerId: id);
            player.UpdateFields($"Player{i}", "Test", Position.WR, currentAge, 6, 80 + i);
            players.Add(player);

            for (var age = DebutAge; age <= exitAge; age++)
            {
                // age = currentAge - (refYear - season)  ⇒  season = refYear - currentAge + age
                var season = refYear - currentAge + age;
                var fppg = talent * TrueAgingFactor(age);

                for (var week = 1; week <= WeeksPerSeason; week++)
                {
                    logs.Add(new PlayerGameLogDocument
                    {
                        PlayerId = id,
                        SleeperPlayerId = id,
                        Position = "WR",
                        Season = season,
                        Week = week,
                        FantasyPointsPpr = (decimal)fppg
                    });
                }
            }
        }

        return (logs, players);
    }
}

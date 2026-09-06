// FF.Tests/Application/Dynasty/CareerSimulationRookiePriorTests.cs
//
// 2026-09-07. Pins the two defects that put Joe Fagnano — Baltimore's QB3, a
// quarterback who has never taken an NFL snap — at #1 on the dynasty board with
// TradeValue 94.5, ahead of Patrick Mahomes.
//
//   1. Every guard in ApplyShrinkage tested `rawFppg <= 0`, meaning NO data at
//      all. Fagnano had a 2026 Week-0 simulation row with a median of 0.17,
//      which was enough to bypass all of them and drop him into the standard
//      blend — where a rookie's credibility of zero hands him 100% of the
//      STARTER prior, 18.5 FPPG, for a five-year career. Having a little
//      evidence was strictly worse than having none.
//
//   2. Week 0 is an overloaded sentinel. The season-average seed writes it for
//      seasons actually played; the projection run writes it for the season
//      ahead. Career simulation was reading the second kind as a track record.

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

public class CareerSimulationRookiePriorTests
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
        string sleeperId, Position pos, int age, int yearsExperience,
        int? draftRound = null, string first = "Test", string last = "Player")
    {
        var p = Player.Create(first, last, pos, sleeperPlayerId: sleeperId);
        p.UpdateFields(first, last, pos, age, yearsExperience, 88);
        if (draftRound.HasValue) p.UpdateDraftCapital(draftRound, 1, "Somewhere");
        return p;
    }

    private void SetupPlayers(Position pos, params Player[] players)
    {
        _playerRepo
            .Setup(r => r.GetByPositionAsync(pos, It.IsAny<CancellationToken>()))
            .ReturnsAsync(players.ToList());
        _playerRepo
            .Setup(r => r.GetByPositionAsync(It.Is<Position>(p => p != pos), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
    }

    private void SetupSims(params SimulationResultDocument[] sims) =>
        _simResultRepo
            .Setup(r => r.GetAllSeasonAveragesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(sims.ToList());

    private void SetupNoCurve() =>
        _agingCurveRepo
            .Setup(r => r.GetByPositionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AgingCurveDocument?)null);

    private static SimulationResultDocument Sim(
        string sleeperId, string name, string pos, int season, decimal median,
        string playerRole = "SeasonAverage", int week = 0) => new()
        {
            SleeperPlayerId = sleeperId,
            PlayerName = name,
            Position = pos,
            Season = season,
            Week = week,
            Median = median,
            Mean = median,
            BaseProjection = median,
            Floor = median * 0.6m,
            Ceiling = median * 1.5m,
            PlayerRole = playerRole
        };

    // ── The bug ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task UndraftedRookieWithTinyProjection_DoesNotOutrankAProvenStarter()
    {
        // Fagnano: age 25, zero experience, undrafted, and a 0.17 projection that
        // used to be his ticket past every guard in the method.
        var fagnano = MakePlayer("13350", Position.QB, 25, 0, draftRound: null, "Joe", "Fagnano");
        var mahomes = MakePlayer("4046", Position.QB, 30, 8, draftRound: 1, "Patrick", "Mahomes");

        SetupPlayers(Position.QB, fagnano, mahomes);
        SetupNoCurve();
        SetupSims(
            Sim("13350", "Joe Fagnano", "QB", 2026, 0.17m, playerRole: "Unknown"),
            Sim("4046", "Patrick Mahomes", "QB", 2025, 21.5m));

        var result = await CreateSut().SimulateAllPlayersAsync(2026);

        var f = result.Single(r => r.SleeperPlayerId == "13350");
        var m = result.Single(r => r.SleeperPlayerId == "4046");

        f.CareerValueScore.Should().BeLessThan(m.CareerValueScore);
    }

    [Fact]
    public async Task ATinyProjectionIsNotBetterThanNoDataAtAll()
    {
        // The perverse property at the heart of the bug: two identical undrafted
        // rookies, one with a 0.17 row and one with nothing, must not be ranked
        // in that order. Before the fix the one WITH data scored far higher,
        // because his 0.17 bypassed the gate that would have floored him.
        var withTinyData = MakePlayer("a1", Position.QB, 25, 0, draftRound: null, "Tiny", "Data");
        var withNoData = MakePlayer("a2", Position.QB, 25, 0, draftRound: null, "No", "Data");

        SetupPlayers(Position.QB, withTinyData, withNoData);
        SetupNoCurve();
        SetupSims(Sim("a1", "Tiny Data", "QB", 2026, 0.17m, playerRole: "Unknown"));

        var result = await CreateSut().SimulateAllPlayersAsync(2026);

        var tiny = result.Single(r => r.SleeperPlayerId == "a1");
        var none = result.Single(r => r.SleeperPlayerId == "a2");

        // Approximately, not "less than or equal": these two are now the same
        // player as far as the model is concerned — both floored to the 6.0 QB
        // depth level — but they draw from independently seeded RNGs, so they
        // land a point or so apart and neither ordering is meaningful.
        //
        // The tolerance still discriminates by two orders of magnitude. Before
        // the fix the one WITH the 0.17 row was modelled at 18.5 FPPG against
        // the other's 6.0, a career-value gap of roughly 650 points. Five is not
        // a loose assertion here; it is 130x smaller than the defect.
        tiny.CareerValueScore.Should().BeApproximately(none.CareerValueScore, 5.0);
    }

    [Fact]
    public async Task CurrentSeasonProjectionRows_AreNotReadAsATrackRecord()
    {
        // Week 0 is written by BOTH the season-average seed and the projection
        // run. Only the first is a season the player actually played.
        var rookie = MakePlayer("b1", Position.QB, 24, 0, draftRound: null, "Projected", "Only");

        SetupPlayers(Position.QB, rookie);
        SetupNoCurve();
        SetupSims(Sim("b1", "Projected Only", "QB", 2026, 14.0m, playerRole: "Unknown"));

        var result = await CreateSut().SimulateAllPlayersAsync(2026);

        // 14.0 would clear the QB depth level of 6.0 and earn him the blend. It
        // must not, because that row is a projection, not a season.
        var doc = result.Single();
        doc.YearProjections[0].MedianFppg.Should().BeLessThan(14.0);
    }

    // ── Draft capital is a curve, not a gate ─────────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(7)]
    public async Task DraftPedigree_OrdersZeroExperienceRookies(int round)
    {
        // Each round should be worth strictly less than the one before it. The
        // old code had two disagreeing gates: QB needed round 1 for the full
        // prior, every other position needed only *a* round — which is how a
        // late-round tight end was modelled as a proven TE1.
        var earlier = MakePlayer("c1", Position.TE, 23, 0, draftRound: round, "Earlier", "Pick");
        var undrafted = MakePlayer("c2", Position.TE, 23, 0, draftRound: null, "Un", "Drafted");

        SetupPlayers(Position.TE, earlier, undrafted);
        SetupNoCurve();
        SetupSims();

        var result = await CreateSut().SimulateAllPlayersAsync(2026);

        var drafted = result.Single(r => r.SleeperPlayerId == "c1");
        var udfa = result.Single(r => r.SleeperPlayerId == "c2");

        drafted.CareerValueScore.Should().BeGreaterThan(udfa.CareerValueScore);
    }

    [Fact]
    public async Task FirstRoundRookie_StillEarnsTheFullStarterPrior()
    {
        // The fix must not flatten legitimate prospects. A 1st-round rookie with
        // no data is exactly who the starter prior is for.
        var firstRounder = MakePlayer("d1", Position.QB, 22, 0, draftRound: 1, "First", "Rounder");
        var undrafted = MakePlayer("d2", Position.QB, 22, 0, draftRound: null, "Un", "Drafted");

        SetupPlayers(Position.QB, firstRounder, undrafted);
        SetupNoCurve();
        SetupSims();

        var result = await CreateSut().SimulateAllPlayersAsync(2026);

        var first = result.Single(r => r.SleeperPlayerId == "d1");
        var udfa = result.Single(r => r.SleeperPlayerId == "d2");

        first.CareerValueScore.Should().BeGreaterThan(udfa.CareerValueScore * 1.5);
    }

    // ── The population that must not move ────────────────────────────────────

    [Fact]
    public async Task ProvenVeterans_AreUnaffected_TheirPriorWeightIsUnchanged()
    {
        // The pedigree scaling applies only at zero experience, so the FAN-95
        // calibration on the veteran population sees no movement from this. A
        // 1st-round veteran and an undrafted veteran with identical production
        // must score the same.
        //
        // "The same" and not "identical": each player's RNG is seeded from his
        // own id, so two players with the same inputs still draw independent
        // samples and land a fraction apart — this assertion first failed at
        // 624.1 vs 623.9. That gap is sampling, not pedigree leaking into the
        // veteran path. It is now reproducible run to run (see
        // SameInputs_ProduceTheSameCareerValue_EveryRun below), just not
        // identical between two different players.
        //
        // The tolerance is set to discriminate, not to paper over. If pedigree
        // DID leak here, an undrafted receiver's prior would fall from 9.0 to
        // the 4.5 depth level; at 5 years the credibility weight on the prior is
        // 0.375, so his blended baseline would drop ~1.7 FPPG and his career
        // value by roughly 100 points. Five is comfortably below that and
        // comfortably above the noise.
        var drafted = MakePlayer("e1", Position.WR, 27, 5, draftRound: 1, "Drafted", "Vet");
        var undrafted = MakePlayer("e2", Position.WR, 27, 5, draftRound: null, "Undrafted", "Vet");

        SetupPlayers(Position.WR, drafted, undrafted);
        SetupNoCurve();
        SetupSims(
            Sim("e1", "Drafted Vet", "WR", 2025, 15.0m),
            Sim("e2", "Undrafted Vet", "WR", 2025, 15.0m));

        var result = await CreateSut().SimulateAllPlayersAsync(2026);

        var a = result.Single(r => r.SleeperPlayerId == "e1");
        var b = result.Single(r => r.SleeperPlayerId == "e2");

        a.CareerValueScore.Should().BeApproximately(b.CareerValueScore, 5.0);
    }

    // ── Determinism ──────────────────────────────────────────────────────────

    [Fact]
    public async Task SameInputs_ProduceTheSameCareerValue_EveryRun()
    {
        // SimulatePlayer used to build `new Random()` unseeded, so a career sim
        // drew a different answer every run on identical data. TradeValue is
        // derived from CareerValueScore, so the dynasty board reshuffled on each
        // recalculation and every calibration delta measured against FantasyPros
        // consensus carried dice in it.
        var players = new[]
        {
            MakePlayer("g1", Position.WR, 25, 3, draftRound: 2, "Repeat", "Able"),
            MakePlayer("g2", Position.WR, 29, 7, draftRound: 4, "Repeat", "Baker")
        };

        SetupPlayers(Position.WR, players);
        SetupNoCurve();
        SetupSims(
            Sim("g1", "Repeat Able", "WR", 2025, 13.0m),
            Sim("g2", "Repeat Baker", "WR", 2025, 11.0m));

        var first = await CreateSut().SimulateAllPlayersAsync(2026);
        var second = await CreateSut().SimulateAllPlayersAsync(2026);

        foreach (var a in first)
        {
            var b = second.Single(x => x.SleeperPlayerId == a.SleeperPlayerId);
            b.CareerValueScore.Should().Be(a.CareerValueScore);
            b.PeakYearValue.Should().Be(a.PeakYearValue);
        }
    }

    [Fact]
    public async Task DifferentSeasons_DrawDifferentCareers()
    {
        // The seed folds in the season, so re-running for a different season is
        // a genuinely new draw rather than a replay of the same one.
        var player = MakePlayer("h1", Position.RB, 24, 2, draftRound: 2, "Season", "Varies");

        SetupPlayers(Position.RB, player);
        SetupNoCurve();
        SetupSims(Sim("h1", "Season Varies", "RB", 2025, 14.0m));

        var a = await CreateSut().SimulateAllPlayersAsync(2026);
        var b = await CreateSut().SimulateAllPlayersAsync(2027);

        // Compare the whole five-year sequence rather than the single rolled-up
        // score — two independent draws can round to the same total often enough
        // to make a one-value assertion flaky, while five matching in a row
        // would mean the seed genuinely didn't change.
        var seriesA = a.Single().YearProjections.Select(y => y.MedianFppg).ToList();
        var seriesB = b.Single().YearProjections.Select(y => y.MedianFppg).ToList();

        seriesA.Should().NotEqual(seriesB);
    }

    [Fact]
    public async Task ExperiencedPlayerWithNoProduction_IsStillTreatedAsACareerBackup()
    {
        // Unchanged behaviour, pinned so the rewrite above cannot quietly drop it.
        var backup = MakePlayer("f1", Position.QB, 28, 4, draftRound: 4, "Career", "Backup");
        var starter = MakePlayer("f2", Position.QB, 28, 4, draftRound: 4, "Real", "Starter");

        SetupPlayers(Position.QB, backup, starter);
        SetupNoCurve();
        SetupSims(Sim("f2", "Real Starter", "QB", 2025, 20.0m));

        var result = await CreateSut().SimulateAllPlayersAsync(2026);

        var b = result.Single(r => r.SleeperPlayerId == "f1");
        var s = result.Single(r => r.SleeperPlayerId == "f2");

        b.CareerValueScore.Should().BeLessThan(s.CareerValueScore);
    }
}

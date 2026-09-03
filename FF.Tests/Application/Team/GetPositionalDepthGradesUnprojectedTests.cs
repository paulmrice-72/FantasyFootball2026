// FF.Tests/Application/Team/GetPositionalDepthGradesUnprojectedTests.cs
//
// Covers the 2026-09-02 changes to GetPositionalDepthGradesQueryHandler:
//   - a missing projection is no longer scored as 0.0
//   - a position with fewer projected players than starter slots is reported
//     ungraded rather than graded on whatever happens to be left
//   - HealthyCount means "not on the injury report", which is what the UI says
//
// Kept separate from GetPositionalDepthGradesQueryHandlerTests so the existing
// suite stays untouched.

using FF.Application.Features.Team.Queries;
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Domain.Entities;
using FF.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace FF.Tests.Application.Team;

public class GetPositionalDepthGradesUnprojectedTests
{
    private readonly Mock<IRosterPlayerRepository> _rosterRepo = new();
    private readonly Mock<IPlayerRepository> _playerRepo = new();
    private readonly Mock<ISimulationResultRepository> _simRepo = new();
    private readonly Mock<IInjuryAlertRepository> _injuryRepo = new();
    private readonly Mock<ILogger<GetPositionalDepthGradesQueryHandler>> _logger = new();

    private GetPositionalDepthGradesQueryHandler CreateHandler() =>
        new(_rosterRepo.Object, _playerRepo.Object, _simRepo.Object,
            _injuryRepo.Object, _logger.Object);

    private static RosterPlayerDocument MakeRoster(params string[] ids) => new()
    {
        SleeperUserId = "user1",
        SleeperLeagueId = "league1",
        TeamName = "Test Team",
        OwnerName = "Paul",
        PlayerIds = ids.ToList(),
        StarterIds = ids.Take(1).ToList()
    };

    private static Player MakePlayer(string sleeperId, string pos) =>
        Player.Create("First", "Last", Enum.Parse<Position>(pos), "KC", sleeperId, null, null);

    private static SimulationResultDocument MakeSim(string sleeperId, decimal median) => new()
    {
        SleeperPlayerId = sleeperId,
        Median = median,
        Floor = median * 0.6m,
        Ceiling = median * 1.5m,
        BoomProbability = 0.2m,
        BustProbability = 0.15m
    };

    private void SetupRoster(params string[] ids) =>
        _rosterRepo
            .Setup(r => r.GetBySleeperUserIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeRoster(ids));

    private void SetupPlayers(params (string Id, string Pos)[] players) =>
        _playerRepo
            .Setup(r => r.GetBySleeperIdsAsync(
                It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(players.Select(p => MakePlayer(p.Id, p.Pos)).ToList());

    private void SetupSims(params (string Id, decimal Median)[] sims) =>
        _simRepo
            .Setup(r => r.GetLatestBySleeperIdsAsync(
                It.IsAny<List<string>>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sims.Select(s => MakeSim(s.Id, s.Median)).ToList());

    private void SetupInjuries(params (string Id, string Designation)[] injuries) =>
        _injuryRepo
            .Setup(r => r.GetActiveAlertsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(injuries.Select(i => new InjuryAlertDocument
            {
                SleeperPlayerId = i.Id,
                Designation = i.Designation,
                PlayerName = "Test Player"
            }).ToList());

    private Task<PositionalDepthGradesDto?> Run() =>
        CreateHandler().Handle(
            new GetPositionalDepthGradesQuery("user1", "league1", 2026), CancellationToken.None);

    // ── Unprojected players ───────────────────────────────────────────────────

    [Fact]
    public async Task FewerProjectedThanStarterSlots_IsReportedUngraded()
    {
        // RB needs 2 starters. Only rb1 has a projection — rb2 is the Kenneth
        // Walker case. The old code scored rb2 as 0.0 and produced a confident
        // letter from (18 + 0) / 2.
        SetupRoster("qb1", "rb1", "rb2", "wr1", "wr2", "wr3", "te1");
        SetupPlayers(
            ("qb1", "QB"), ("rb1", "RB"), ("rb2", "RB"),
            ("wr1", "WR"), ("wr2", "WR"), ("wr3", "WR"), ("te1", "TE"));
        SetupSims(
            ("qb1", 20m), ("rb1", 18m),
            ("wr1", 14m), ("wr2", 12m), ("wr3", 10m), ("te1", 11m));
        SetupInjuries();

        var rb = (await Run())!.Grades.Single(g => g.Position == "RB");

        rb.Grade.Should().Be("—");
        rb.Label.Should().Be("Not graded");
        rb.GradeScore.Should().Be(0);
        rb.Summary.Should().Contain("not graded");
        rb.RosteredCount.Should().Be(2);   // rb2 is still on the roster
    }

    [Fact]
    public async Task EnoughProjectedStarters_StillGrades_AndDisclosesTheGap()
    {
        // rb3 has no projection, but both starting slots are covered, so the
        // position is judgeable — it just says what it could not see.
        SetupRoster("qb1", "rb1", "rb2", "rb3", "wr1", "wr2", "wr3", "te1");
        SetupPlayers(
            ("qb1", "QB"), ("rb1", "RB"), ("rb2", "RB"), ("rb3", "RB"),
            ("wr1", "WR"), ("wr2", "WR"), ("wr3", "WR"), ("te1", "TE"));
        SetupSims(
            ("qb1", 20m), ("rb1", 18m), ("rb2", 14m),
            ("wr1", 14m), ("wr2", 12m), ("wr3", 10m), ("te1", 11m));
        SetupInjuries();

        var rb = (await Run())!.Grades.Single(g => g.Position == "RB");

        rb.Grade.Should().NotBe("—");
        rb.StarterScore.Should().Be(16.0);     // (18 + 14) / 2 — rb3 excluded, not zeroed
        rb.RosteredCount.Should().Be(3);
        rb.Summary.Should().Contain("without a projection");
    }

    [Fact]
    public async Task UnprojectedPlayer_DoesNotContributeToDepth()
    {
        SetupRoster("qb1", "qb2");
        SetupPlayers(("qb1", "QB"), ("qb2", "QB"));
        SetupSims(("qb1", 20m));           // qb2 unprojected
        SetupInjuries();

        var withMissing = (await Run())!.Grades.Single(g => g.Position == "QB");

        SetupSims(("qb1", 20m), ("qb2", 18m));
        var withBackup = (await Run())!.Grades.Single(g => g.Position == "QB");

        withBackup.GradeScore.Should().BeGreaterThan(withMissing.GradeScore);
        withMissing.StarterScore.Should().Be(20.0);   // unchanged by the missing backup
    }

    // ── Health counting ───────────────────────────────────────────────────────

    [Fact]
    public async Task QuestionablePlayer_IsNotCountedAsHealthy()
    {
        // Was the visible bug: three Questionable players rendering as 4/4, 6/6, 2/2.
        SetupRoster("qb1", "qb2");
        SetupPlayers(("qb1", "QB"), ("qb2", "QB"));
        SetupSims(("qb1", 20m), ("qb2", 18m));
        SetupInjuries(("qb1", "Questionable"));

        var qb = (await Run())!.Grades.Single(g => g.Position == "QB");

        qb.RosteredCount.Should().Be(2);
        qb.HealthyCount.Should().Be(1);
    }

    [Fact]
    public async Task QuestionablePlayer_StillCountsTowardTheGrade()
    {
        // Not healthy is not the same as not playing — a Q player still suits up
        // most weeks, so he keeps his 0.80-weighted contribution.
        SetupRoster("qb1");
        SetupPlayers(("qb1", "QB"));
        SetupSims(("qb1", 20m));
        SetupInjuries(("qb1", "Questionable"));

        var qb = (await Run())!.Grades.Single(g => g.Position == "QB");

        qb.Grade.Should().NotBe("—");
        qb.StarterScore.Should().Be(16.0);   // 20 * 0.80
    }

    [Fact]
    public async Task ShortDesignation_Q_IsTreatedTheSameAsQuestionable()
    {
        // qb2 is deliberately well below qb1's haircut value (20 * 0.80 = 16.0) so
        // qb1 stays the starter and StarterScore isolates the 0.80 factor. Setting
        // qb2 to 18 instead would make HIM the starter — correctly, but then this
        // test would be measuring the ranking rather than the designation parsing.
        SetupRoster("qb1", "qb2");
        SetupPlayers(("qb1", "QB"), ("qb2", "QB"));
        SetupSims(("qb1", 20m), ("qb2", 10m));
        SetupInjuries(("qb1", "Q"));

        var qb = (await Run())!.Grades.Single(g => g.Position == "QB");

        qb.HealthyCount.Should().Be(1);      // "Q" normalises to Questionable
        qb.StarterScore.Should().Be(16.0);   // 20 * 0.80
    }

    [Fact]
    public async Task HealthyBackup_OutranksAQuestionableStarter()
    {
        // The behaviour that broke the test above, pinned deliberately. This is the
        // Tucker-over-Brian-Thomas case from the live WR card on 2026-09-02.
        SetupRoster("qb1", "qb2");
        SetupPlayers(("qb1", "QB"), ("qb2", "QB"));
        SetupSims(("qb1", 20m), ("qb2", 18m));
        SetupInjuries(("qb1", "Q"));

        var qb = (await Run())!.Grades.Single(g => g.Position == "QB");

        qb.StarterScore.Should().Be(18.0);   // qb2, healthy, beats 20 * 0.80 = 16.0
        qb.HealthyCount.Should().Be(1);
    }

    [Fact]
    public async Task OutPlayer_IsNeitherHealthyNorScored()
    {
        SetupRoster("qb1", "qb2");
        SetupPlayers(("qb1", "QB"), ("qb2", "QB"));
        SetupSims(("qb1", 20m), ("qb2", 18m));
        SetupInjuries(("qb1", "Out"));

        var qb = (await Run())!.Grades.Single(g => g.Position == "QB");

        qb.HealthyCount.Should().Be(1);
        qb.StarterScore.Should().Be(18.0);   // qb2 becomes the starter
    }
}

// FF.Tests/Application/Team/GetPositionalDepthGradesQueryHandlerTests.cs
using FF.Application.Features.Team.Queries;
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Domain.Entities;
using FF.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace FF.Tests.Application.Team;

public class GetPositionalDepthGradesQueryHandlerTests
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
        Player.Create(
            firstName: "First",
            lastName: "Last",
            position: Enum.Parse<Position>(pos),
            nflTeam: "KC",
            sleeperPlayerId: sleeperId,
            gsisId: null,
            collegeTeam: null);

    private static SimulationResultDocument MakeSim(string sleeperId, decimal median) => new()
    {
        SleeperPlayerId = sleeperId,
        Median = median,
        Floor = median * 0.6m,
        Ceiling = median * 1.5m,
        BoomProbability = 0.2m,
        BustProbability = 0.15m
    };

    // ── Helpers ───────────────────────────────────────────────────────────────

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

    private void SetupNoInjuries() =>
        _injuryRepo
            .Setup(r => r.GetActiveAlertsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

    // ── Existing tests (unchanged behaviour) ──────────────────────────────────

    [Fact]
    public async Task Handle_NullRoster_ReturnsNull()
    {
        _rosterRepo
            .Setup(r => r.GetBySleeperUserIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RosterPlayerDocument?)null);

        var result = await CreateHandler().Handle(
            new GetPositionalDepthGradesQuery("u", "l", 2024), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PopulatedRoster_ReturnsFourPositionGrades()
    {
        SetupRoster("qb1", "rb1", "wr1", "te1");
        SetupPlayers(("qb1", "QB"), ("rb1", "RB"), ("wr1", "WR"), ("te1", "TE"));
        SetupSims(("qb1", 22m), ("rb1", 14m), ("wr1", 12m), ("te1", 9m));
        SetupNoInjuries();

        var result = await CreateHandler().Handle(
            new GetPositionalDepthGradesQuery("user1", "league1", 2024), CancellationToken.None);

        result.Should().NotBeNull();
        result!.Grades.Should().HaveCount(4);
        result.Grades.Select(g => g.Position).Should()
            .BeEquivalentTo(["QB", "RB", "WR", "TE"]);
    }

    [Fact]
    public async Task Handle_EliteQB_EarnsHighGrade()
    {
        SetupRoster("qb1", "rb1", "wr1", "te1");
        SetupPlayers(("qb1", "QB"), ("rb1", "RB"), ("wr1", "WR"), ("te1", "TE"));
        SetupSims(("qb1", 36m), ("rb1", 10m), ("wr1", 9m), ("te1", 7m));
        SetupNoInjuries();

        var result = await CreateHandler().Handle(
            new GetPositionalDepthGradesQuery("user1", "league1", 2024), CancellationToken.None);

        var qbGrade = result!.Grades.Single(g => g.Position == "QB");
        qbGrade.Grade.Should().BeOneOf("A+", "A", "B+");
        qbGrade.GradeScore.Should().BeGreaterThan(60);
    }

    [Fact]
    public async Task Handle_InjuredStarter_ReducesGradeScore()
    {
        SetupRoster("qb1", "rb1", "wr1", "te1");
        SetupPlayers(("qb1", "QB"), ("rb1", "RB"), ("wr1", "WR"), ("te1", "TE"));
        SetupSims(("qb1", 20m), ("rb1", 10m), ("wr1", 9m), ("te1", 7m));

        _injuryRepo
            .Setup(r => r.GetActiveAlertsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new InjuryAlertDocument
                { SleeperPlayerId = "qb1", Designation = "Out", PlayerName = "Test QB" }]);

        var result = await CreateHandler().Handle(
            new GetPositionalDepthGradesQuery("user1", "league1", 2024), CancellationToken.None);

        var qbGrade = result!.Grades.Single(g => g.Position == "QB");
        qbGrade.GradeScore.Should().BeLessThan(30);
    }

    [Fact]
    public async Task Handle_GradeScores_AreClampedTo100()
    {
        SetupRoster("qb1", "rb1", "wr1", "te1");
        SetupPlayers(("qb1", "QB"), ("rb1", "RB"), ("wr1", "WR"), ("te1", "TE"));
        SetupSims(("qb1", 999m), ("rb1", 999m), ("wr1", 999m), ("te1", 999m));
        SetupNoInjuries();

        var result = await CreateHandler().Handle(
            new GetPositionalDepthGradesQuery("user1", "league1", 2024), CancellationToken.None);

        result!.Grades.Should().AllSatisfy(g => g.GradeScore.Should().BeLessThanOrEqualTo(100));
    }

    // ── New tests for filler floor ────────────────────────────────────────────

    [Fact]
    public async Task Handle_FillerTEsBelow40PctBaseline_DoNotInflateDepthGrade()
    {
        // TE baseline = 12.1; 40% floor = 4.84 pts
        // te1 (starter) = 9.7 — legit starter, always counts
        // te2/te3/te4 = 2.0 — filler, below floor, should contribute 0
        SetupRoster("qb1", "rb1", "wr1", "te1", "te2", "te3", "te4");
        SetupPlayers(
            ("qb1", "QB"), ("rb1", "RB"), ("wr1", "WR"),
            ("te1", "TE"), ("te2", "TE"), ("te3", "TE"), ("te4", "TE"));
        SetupSims(
            ("qb1", 20m), ("rb1", 15m), ("wr1", 13m),
            ("te1", 9.7m), ("te2", 2m), ("te3", 2m), ("te4", 1m));
        SetupNoInjuries();

        var result = await CreateHandler().Handle(
            new GetPositionalDepthGradesQuery("user1", "league1", 2024), CancellationToken.None);

        var teGrade = result!.Grades.Single(g => g.Position == "TE");

        // With filler floor: only te1 contributes to depth score
        // starterNorm = (9.7/12.1)*50 = ~40; depthNorm = (9.7/12.1)*30 = ~24 → total ~64 = B+
        // Without floor: te2/te3/te4 would inflate depthNorm to ~78+ = A
        teGrade.Grade.Should().BeOneOf("B+", "B", "C+"); // NOT A or A+
        teGrade.GradeScore.Should().BeLessThan(78);       // below A threshold
    }

    [Fact]
    public async Task Handle_QualityBackupAboveFloor_StillContributesToDepthScore()
    {
        // RB baseline = 15.1; 40% floor = 6.04 pts
        // rb1 (starter slot 1) = 18.0 — always counts
        // rb2 (starter slot 2) = 10.0 — above floor, always counts as starter
        // rb3 (backup) = 10.0 — above floor, contributes to depth
        SetupRoster("qb1", "rb1", "rb2", "rb3", "wr1", "te1");
        SetupPlayers(
            ("qb1", "QB"), ("rb1", "RB"), ("rb2", "RB"), ("rb3", "RB"),
            ("wr1", "WR"), ("te1", "TE"));
        SetupSims(
            ("qb1", 20m), ("rb1", 18m), ("rb2", 14m), ("rb3", 10m),
            ("wr1", 13m), ("te1", 9m));
        SetupNoInjuries();

        var result = await CreateHandler().Handle(
            new GetPositionalDepthGradesQuery("user1", "league1", 2024), CancellationToken.None);

        var rbGrade = result!.Grades.Single(g => g.Position == "RB");

        // rb3 at 10pts (above 6.04 floor) contributes — depth should be solid
        rbGrade.Grade.Should().BeOneOf("A+", "A", "B+", "B");
        rbGrade.GradeScore.Should().BeGreaterThan(52); // at minimum B territory
    }

    [Fact]
    public async Task Handle_FillerBackupBelowFloor_DoesNotContributeToDepthScore()
    {
        // RB baseline = 15.1; 40% floor = 6.04 pts
        // rb1 (starter) = 18.0, rb2 (starter) = 14.0 — both starter slots, always count
        // rb3 (backup) = 3.0 — below floor, contributes 0
        SetupRoster("qb1", "rb1", "rb2", "rb3", "wr1", "te1");
        SetupPlayers(
            ("qb1", "QB"), ("rb1", "RB"), ("rb2", "RB"), ("rb3", "RB"),
            ("wr1", "WR"), ("te1", "TE"));
        SetupSims(
            ("qb1", 20m), ("rb1", 18m), ("rb2", 14m), ("rb3", 3m),
            ("wr1", 13m), ("te1", 9m));
        SetupNoInjuries();

        var withFiller = await CreateHandler().Handle(
            new GetPositionalDepthGradesQuery("user1", "league1", 2024), CancellationToken.None);

        // Now same roster but rb3 = 10.0 (above floor)
        SetupSims(
            ("qb1", 20m), ("rb1", 18m), ("rb2", 14m), ("rb3", 10m),
            ("wr1", 13m), ("te1", 9m));

        var withQualityBackup = await CreateHandler().Handle(
            new GetPositionalDepthGradesQuery("user1", "league1", 2024), CancellationToken.None);

        var fillerGrade = withFiller!.Grades.Single(g => g.Position == "RB");
        var qualityGrade = withQualityBackup!.Grades.Single(g => g.Position == "RB");

        // Quality backup (10pts) should score higher than filler (3pts)
        qualityGrade.GradeScore.Should().BeGreaterThan(fillerGrade.GradeScore);
    }

    [Fact]
    public async Task Handle_StarterAlwaysCountsRegardlessOfProjection()
    {
        // Even if the starter projects below the filler floor (injured backup filling in),
        // the starter slot always contributes — only depth slots are gated.
        // TE baseline = 12.1; floor = 4.84; starter projecting 3.0 (below floor)
        SetupRoster("qb1", "rb1", "wr1", "te1");
        SetupPlayers(("qb1", "QB"), ("rb1", "RB"), ("wr1", "WR"), ("te1", "TE"));
        SetupSims(("qb1", 20m), ("rb1", 15m), ("wr1", 13m), ("te1", 3m));
        SetupNoInjuries();

        var result = await CreateHandler().Handle(
            new GetPositionalDepthGradesQuery("user1", "league1", 2024), CancellationToken.None);

        var teGrade = result!.Grades.Single(g => g.Position == "TE");

        // te1 (starter slot) always counts — score is low but not zero
        teGrade.GradeScore.Should().BeGreaterThan(0);
        teGrade.StarterScore.Should().Be(3.0);
    }
}
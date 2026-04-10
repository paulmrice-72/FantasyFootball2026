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
        new(_rosterRepo.Object, _playerRepo.Object,
            _simRepo.Object, _injuryRepo.Object, _logger.Object);

    private static RosterPlayerDocument MakeRoster(params string[] ids) =>
        new()
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

    private static SimulationResultDocument MakeSim(string sleeperId, decimal median) =>
        new()
        {
            SleeperPlayerId = sleeperId,
            Median = median,
            Floor = median * 0.6m,
            Ceiling = median * 1.5m,
            BoomProbability = 0.2m,
            BustProbability = 0.15m
        };

    [Fact]
    public async Task Handle_NullRoster_ReturnsNull()
    {
        _rosterRepo.Setup(r => r.GetBySleeperUserIdAsync(It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RosterPlayerDocument?)null);

        var result = await CreateHandler().Handle(
            new GetPositionalDepthGradesQuery("u", "l", 2024),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PopulatedRoster_ReturnsFourPositionGrades()
    {
        _rosterRepo.Setup(r => r.GetBySleeperUserIdAsync(It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeRoster("qb1", "rb1", "wr1", "te1"));

        _playerRepo.Setup(r => r.GetBySleeperIdsAsync(It.IsAny<List<string>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakePlayer("qb1", "QB"),
                MakePlayer("rb1", "RB"),
                MakePlayer("wr1", "WR"),
                MakePlayer("te1", "TE")
            ]);

        _simRepo.Setup(r => r.GetLatestBySleeperIdsAsync(It.IsAny<List<string>>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeSim("qb1", 22m),
                MakeSim("rb1", 14m),
                MakeSim("wr1", 12m),
                MakeSim("te1", 9m)
            ]);

        _injuryRepo.Setup(r => r.GetActiveAlertsAsync(It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateHandler().Handle(
            new GetPositionalDepthGradesQuery("user1", "league1", 2024),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Grades.Should().HaveCount(4);
        result.Grades.Select(g => g.Position).Should()
            .BeEquivalentTo(["QB", "RB", "WR", "TE"]);
    }

    [Fact]
    public async Task Handle_EliteQB_EarnsHighGrade()
    {
        _rosterRepo.Setup(r => r.GetBySleeperUserIdAsync(It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeRoster("qb1", "rb1", "wr1", "te1"));

        _playerRepo.Setup(r => r.GetBySleeperIdsAsync(It.IsAny<List<string>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakePlayer("qb1", "QB"),
                MakePlayer("rb1", "RB"),
                MakePlayer("wr1", "WR"),
                MakePlayer("te1", "TE")
            ]);

        // QB projecting 36 pts — far above 18pt baseline
        _simRepo.Setup(r => r.GetLatestBySleeperIdsAsync(It.IsAny<List<string>>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeSim("qb1", 36m),
                MakeSim("rb1", 10m),
                MakeSim("wr1", 9m),
                MakeSim("te1", 7m)
            ]);

        _injuryRepo.Setup(r => r.GetActiveAlertsAsync(It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateHandler().Handle(
            new GetPositionalDepthGradesQuery("user1", "league1", 2024),
            CancellationToken.None);

        var qbGrade = result!.Grades.Single(g => g.Position == "QB");
        qbGrade.Grade.Should().BeOneOf("A+", "A", "B+");
        qbGrade.GradeScore.Should().BeGreaterThan(60);
    }

    [Fact]
    public async Task Handle_InjuredStarter_ReducesGradeScore()
    {
        _rosterRepo.Setup(r => r.GetBySleeperUserIdAsync(It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeRoster("qb1", "rb1", "wr1", "te1"));

        _playerRepo.Setup(r => r.GetBySleeperIdsAsync(It.IsAny<List<string>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakePlayer("qb1", "QB"),
                MakePlayer("rb1", "RB"),
                MakePlayer("wr1", "WR"),
                MakePlayer("te1", "TE")
            ]);

        _simRepo.Setup(r => r.GetLatestBySleeperIdsAsync(It.IsAny<List<string>>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeSim("qb1", 20m),
                MakeSim("rb1", 10m),
                MakeSim("wr1", 9m),
                MakeSim("te1", 7m)
            ]);

        // QB is Out — should reduce grade vs healthy baseline
        _injuryRepo.Setup(r => r.GetActiveAlertsAsync(It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync([new InjuryAlertDocument
            {
                SleeperPlayerId = "qb1",
                Designation = "Out",
                PlayerName = "Test QB"
            }]);

        var healthyResult = await CreateHandler().Handle(
            new GetPositionalDepthGradesQuery("user1", "league1", 2024),
            CancellationToken.None);

        var qbGrade = healthyResult!.Grades.Single(g => g.Position == "QB");
        // Out player has 0 effective pts — grade should be low
        qbGrade.GradeScore.Should().BeLessThan(30);
    }

    [Fact]
    public async Task Handle_GradeScores_AreClampedTo100()
    {
        _rosterRepo.Setup(r => r.GetBySleeperUserIdAsync(It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeRoster("qb1", "rb1", "wr1", "te1"));

        _playerRepo.Setup(r => r.GetBySleeperIdsAsync(It.IsAny<List<string>>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakePlayer("qb1", "QB"),
                MakePlayer("rb1", "RB"),
                MakePlayer("wr1", "WR"),
                MakePlayer("te1", "TE")
            ]);

        // Absurdly high projections — should clamp at 100, not overflow
        _simRepo.Setup(r => r.GetLatestBySleeperIdsAsync(It.IsAny<List<string>>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeSim("qb1", 999m),
                MakeSim("rb1", 999m),
                MakeSim("wr1", 999m),
                MakeSim("te1", 999m)
            ]);

        _injuryRepo.Setup(r => r.GetActiveAlertsAsync(It.IsAny<string?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateHandler().Handle(
            new GetPositionalDepthGradesQuery("user1", "league1", 2024),
            CancellationToken.None);

        result!.Grades.Should().AllSatisfy(g =>
            g.GradeScore.Should().BeLessThanOrEqualTo(100));
    }
}
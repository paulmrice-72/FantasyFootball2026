// FF.Tests/Application/Team/GetDynastyTeamGradeQueryHandlerTests.cs
using FF.Application.Features.Team.Queries;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using FF.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace FF.Tests.Application.Team;

public class GetDynastyTeamGradeQueryHandlerTests
{
    private readonly Mock<IRosterPlayerRepository> _rosterRepo = new();
    private readonly Mock<IDynastyValuationRepository> _dynastyRepo = new();
    private readonly Mock<ILogger<GetDynastyTeamGradeQueryHandler>> _logger = new();

    private GetDynastyTeamGradeQueryHandler CreateHandler() =>
        new(_rosterRepo.Object, _dynastyRepo.Object, _logger.Object);

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

    private static DynastyValuationDocument MakeValuation(
        string sleeperId, CareerPhase phase, double tradeValue,
        int age = 26, double breakout = 50, double yearsOfPrime = 3) =>
        new()
        {
            SleeperPlayerId = sleeperId,
            CareerPhase = phase,
            TradeValue = tradeValue,
            Age = age,
            BreakoutScore = breakout,
            YearsOfPrimeRemaining = yearsOfPrime,
            PlayerName = "Test Player",
            Position = "WR",
            Season = 2024
        };

    [Fact]
    public async Task Handle_NullRoster_ReturnsNull()
    {
        _rosterRepo.Setup(r => r.GetBySleeperUserIdAsync(It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RosterPlayerDocument?)null);

        var result = await CreateHandler().Handle(
            new GetDynastyTeamGradeQuery("u", "l"),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoValuations_ReturnsNull()
    {
        _rosterRepo.Setup(r => r.GetBySleeperUserIdAsync(It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeRoster("p1", "p2"));

        _dynastyRepo.Setup(r => r.GetBySleeperPlayerIdsAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateHandler().Handle(
            new GetDynastyTeamGradeQuery("user1", "league1"),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AllPrimeNoYouth_ContentionExceedsLongevity()
    {
        _rosterRepo.Setup(r => r.GetBySleeperUserIdAsync(It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeRoster("p1", "p2"));

        // Pure prime roster — no young players at all
        _dynastyRepo.Setup(r => r.GetBySleeperPlayerIdsAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeValuation("p1", CareerPhase.Prime, 75, age: 27),
                MakeValuation("p2", CareerPhase.Prime, 70, age: 28)
            ]);

        var result = await CreateHandler().Handle(
            new GetDynastyTeamGradeQuery("user1", "league1"),
            CancellationToken.None);

        result.Should().NotBeNull();
        // With only Prime players: contention should exceed longevity
        // because young/ascending group is empty (longevity weight = Prime * 0.30 only)
        result!.ContentionScore.Should().BeGreaterThan(result.LongevityScore);
        result.YoungPlayerCount.Should().Be(0);
        result.PrimePlayerCount.Should().Be(2);
    }

    [Fact]
    public async Task Handle_YoungHeavyRoster_HighLongevityScore()
    {
        _rosterRepo.Setup(r => r.GetBySleeperUserIdAsync(It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeRoster("p1", "p2", "p3"));

        _dynastyRepo.Setup(r => r.GetBySleeperPlayerIdsAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeValuation("p1", CareerPhase.Ascending, 70, age: 22, breakout: 80, yearsOfPrime: 6),
                MakeValuation("p2", CareerPhase.Ascending, 65, age: 23, breakout: 75, yearsOfPrime: 5),
                MakeValuation("p3", CareerPhase.Ascending, 60, age: 21, breakout: 70, yearsOfPrime: 7)
            ]);

        var result = await CreateHandler().Handle(
            new GetDynastyTeamGradeQuery("user1", "league1"),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.LongevityScore.Should().BeGreaterThan(50);
        result.YoungPlayerCount.Should().Be(3);
    }



    [Fact]
    public async Task Handle_ScoresClampedTo100()
    {
        _rosterRepo.Setup(r => r.GetBySleeperUserIdAsync(It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeRoster("p1", "p2"));

        _dynastyRepo.Setup(r => r.GetBySleeperPlayerIdsAsync(
            It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                MakeValuation("p1", CareerPhase.Prime, 100, age: 25),
                MakeValuation("p2", CareerPhase.Prime, 100, age: 26)
            ]);

        var result = await CreateHandler().Handle(
            new GetDynastyTeamGradeQuery("user1", "league1"),
            CancellationToken.None);

        result!.ContentionScore.Should().BeLessThanOrEqualTo(100);
        result.LongevityScore.Should().BeLessThanOrEqualTo(100);
    }
}
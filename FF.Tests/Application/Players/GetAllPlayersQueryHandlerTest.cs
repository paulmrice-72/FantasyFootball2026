using FF.Application.Interfaces.Persistence;
using FF.Application.Players.Queries.GetAllPlayers;
using FF.Domain.Entities;
using FF.Domain.Enums;
using FluentAssertions;
using Moq;

namespace FF.Tests.Application.Players;

public class GetAllPlayersQueryHandlerTests
{
    private readonly Mock<IPlayerRepository> _repositoryMock;
    private readonly GetAllPlayersQueryHandler _handler;

    public GetAllPlayersQueryHandlerTests()
    {
        _repositoryMock = new Mock<IPlayerRepository>();
        _handler = new GetAllPlayersQueryHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ReturnsAllPlayers_WhenPlayersExist()
    {
        var players = new List<Player>
        {
            Player.Create("Justin", "Jefferson", Position.WR, "MIN", "sleeper-1"),
            Player.Create("Patrick", "Mahomes", Position.QB, "KC", "sleeper-2")
        };

        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(players);

        var result = await _handler.Handle(new GetAllPlayersQuery(), CancellationToken.None);

        result.TotalCount.Should().Be(2);
        result.Players.Should().HaveCount(2);
        result.Players[0].FullName.Should().Be("Justin Jefferson");
        result.Players[1].Position.Should().Be("QB");
    }

    [Fact]
    public async Task Handle_ReturnsEmptyResponse_WhenNoPlayersExist()
    {
        _repositoryMock
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Player>());

        var result = await _handler.Handle(new GetAllPlayersQuery(), CancellationToken.None);

        result.TotalCount.Should().Be(0);
        result.Players.Should().BeEmpty();
    }
}
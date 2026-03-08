// FF.Tests/Application/Players/SyncPlayersCommandHandlerTests.cs

using FF.Application.Interfaces.Services;
using FF.Application.Players.Commands.SyncPlayers;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FF.Tests.Application.Players;

public class SyncPlayersCommandHandlerTests
{
    private readonly Mock<ISleeperPlayerSyncService> _syncServiceMock;
    private readonly SyncPlayersCommandHandler _handler;

    public SyncPlayersCommandHandlerTests()
    {
        _syncServiceMock = new Mock<ISleeperPlayerSyncService>();
        _handler = new SyncPlayersCommandHandler(
            _syncServiceMock.Object,
            NullLogger<SyncPlayersCommandHandler>.Instance);
    }

    [Fact(DisplayName = "Handle returns success with correct counts when sync succeeds")]
    public async Task Handle_SyncSucceeds_ReturnsSuccessResult()
    {
        // Arrange
        var expectedResult = new SyncPlayersResult(
            PlayersAdded: 150,
            PlayersUpdated: 4850,
            PlayersSkipped: 12000,
            TotalProcessed: 17000,
            Duration: TimeSpan.FromSeconds(18));

        _syncServiceMock
            .Setup(s => s.SyncAllPlayersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _handler.Handle(
            new SyncPlayersCommand(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.PlayersAdded.Should().Be(150);
        result.Value.PlayersUpdated.Should().Be(4850);
        result.Value.PlayersSkipped.Should().Be(12000);
    }

    [Fact(DisplayName = "Handle returns failure result when sync service throws")]
    public async Task Handle_SyncThrows_ReturnsFailureResult()
    {
        // Arrange
        _syncServiceMock
            .Setup(s => s.SyncAllPlayersAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Sleeper API unavailable"));

        // Act
        var result = await _handler.Handle(
            new SyncPlayersCommand(), CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error!.Message.Should().Contain("Sleeper API unavailable");
    }

    [Fact(DisplayName = "Handle calls sync service exactly once")]
    public async Task Handle_ValidCommand_CallsServiceOnce()
    {
        // Arrange
        _syncServiceMock
            .Setup(s => s.SyncAllPlayersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SyncPlayersResult(0, 0, 0, 0, TimeSpan.Zero));

        // Act
        await _handler.Handle(new SyncPlayersCommand(), CancellationToken.None);

        // Assert
        _syncServiceMock.Verify(
            s => s.SyncAllPlayersAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

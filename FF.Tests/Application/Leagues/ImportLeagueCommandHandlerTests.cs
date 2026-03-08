// FF.Tests/Application/Leagues/ImportLeagueCommandHandlerTests.cs

using FF.Application.Interfaces.Services;
using FF.Application.Leagues.Commands.ImportLeague;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FF.Tests.Application.Leagues;

public class ImportLeagueCommandHandlerTests
{
    private readonly Mock<ISleeperLeagueImportService> _importServiceMock;
    private readonly ImportLeagueCommandHandler _handler;

    public ImportLeagueCommandHandlerTests()
    {
        _importServiceMock = new Mock<ISleeperLeagueImportService>();
        _handler = new ImportLeagueCommandHandler(
            _importServiceMock.Object,
            NullLogger<ImportLeagueCommandHandler>.Instance);
    }

    [Fact(DisplayName = "Handle returns success result when import service succeeds")]
    public async Task Handle_ImportSucceeds_ReturnsSuccessResult()
    {
        // Arrange
        var expectedResult = new ImportLeagueResult(
            LeagueName: "Test League",
            LeagueId: "test-league-123",
            RostersImported: 12,
            PlayersImported: 180,
            TransactionsImported: 45,
            WasNewLeague: true);

        _importServiceMock
            .Setup(s => s.ImportLeagueAsync("test-league-123", It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var command = new ImportLeagueCommand("test-league-123");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.LeagueName.Should().Be("Test League");
        result.Value.RostersImported.Should().Be(12);
        result.Value.WasNewLeague.Should().BeTrue();
    }

    [Fact(DisplayName = "Handle returns failure result when import service throws")]
    public async Task Handle_ImportThrows_ReturnsFailureResult()
    {
        // Arrange
        _importServiceMock
            .Setup(s => s.ImportLeagueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("League not found on Sleeper"));

        var command = new ImportLeagueCommand("invalid-league-id");

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Message.Should().Contain("League not found on Sleeper");
    }

    [Fact(DisplayName = "Handle calls import service with correct league ID")]
    public async Task Handle_ValidCommand_PassesLeagueIdToService()
    {
        // Arrange
        const string leagueId = "1326771899543355392";

        _importServiceMock
            .Setup(s => s.ImportLeagueAsync(leagueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportLeagueResult(
                "My League", leagueId, 12, 180, 0, false));

        // Act
        await _handler.Handle(new ImportLeagueCommand(leagueId), CancellationToken.None);

        // Assert - verify it called the service with the exact league ID
        _importServiceMock.Verify(
            s => s.ImportLeagueAsync(leagueId, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

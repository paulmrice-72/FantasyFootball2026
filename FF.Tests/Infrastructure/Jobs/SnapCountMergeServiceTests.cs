using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace FF.Tests.Infrastructure.Jobs;

public class SnapCountMergeServiceTests
{
    private readonly ISnapCountRepository _snapCountRepository;
    private readonly IPlayerGameLogRepository _gameLogRepository;
    private readonly SnapCountMergeService _service;

    public SnapCountMergeServiceTests()
    {
        _snapCountRepository = Substitute.For<ISnapCountRepository>();
        _gameLogRepository = Substitute.For<IPlayerGameLogRepository>();
        _service = new SnapCountMergeService(
            _snapCountRepository,
            _gameLogRepository,
            NullLogger<SnapCountMergeService>.Instance);
    }

    [Fact]
    public async Task MergeAsync_NoSnapCounts_ReturnsFailure()
    {
        _snapCountRepository.GetBySeasonWeekAsync(Arg.Any<int>(), Arg.Any<int>(),
            Arg.Any<CancellationToken>())
            .Returns(new List<SnapCountDocument>());

        var result = await _service.MergeAsync(2024);

        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("No snap counts found");
    }

    [Fact]
    public async Task MergeAsync_MatchingRecords_MergesCorrectly()
    {
        var snapCounts = new List<SnapCountDocument>
        {
            new() { PlayerName = "Justin Jefferson", Team = "MIN",
                    Season = 2024, Week = 1, OffenseSnaps = 65, OffensePct = 0.95m }
        };

        var gameLogs = new List<PlayerGameLogDocument>
        {
            new() { PlayerId = "gsis-123", PlayerName = "Justin Jefferson",
                    NflTeam = "MIN", Season = 2024, Week = 1 }
        };

        _snapCountRepository.GetBySeasonWeekAsync(2024, 1, Arg.Any<CancellationToken>())
            .Returns(snapCounts);
        _snapCountRepository.GetBySeasonWeekAsync(2024, Arg.Is<int>(w => w != 1),
            Arg.Any<CancellationToken>())
            .Returns(new List<SnapCountDocument>());

        _gameLogRepository.GetBySeasonAsync(2024, Arg.Any<CancellationToken>())
            .Returns(gameLogs);

        var result = await _service.MergeAsync(2024);

        result.Success.Should().BeTrue();
        result.Merged.Should().Be(1);
        gameLogs[0].OffenseSnaps.Should().Be(65);
        gameLogs[0].SnapPct.Should().Be(0.95m);
    }
}

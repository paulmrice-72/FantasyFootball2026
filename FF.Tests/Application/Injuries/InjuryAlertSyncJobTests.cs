using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Infrastructure.ExternalApis.Sleeper;
using FF.Infrastructure.ExternalApis.Sleeper.Dtos;
using FF.Infrastructure.Jobs;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using FluentAssertions;
using Xunit;

namespace FF.Tests.Application.Injuries;

public class InjuryAlertSyncJobTests
{
    private readonly Mock<ISleeperApiClient> _sleeperClient = new();
    private readonly Mock<IInjuryAlertRepository> _repo = new();

    private InjuryAlertSyncJob CreateSut() =>
        new(_sleeperClient.Object, _repo.Object,
            NullLogger<InjuryAlertSyncJob>.Instance);

    private static Dictionary<string, SleeperPlayerDto> MakePlayers(
        params SleeperPlayerDto[] dtos) =>
        dtos.ToDictionary(d => d.PlayerId!, d => d);

    [Fact]
    public async Task RunAsync_OnlyPersists_PlayersWithActiveDesignation()
    {
        _sleeperClient
            .Setup(c => c.GetAllPlayersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePlayers(
                new SleeperPlayerDto { PlayerId = "p1", FullName = "Healthy Player", Position = "WR", InjuryStatus = null },
                new SleeperPlayerDto { PlayerId = "p2", FullName = "Questionable WR", Position = "WR", InjuryStatus = "Questionable" },
                new SleeperPlayerDto { PlayerId = "p3", FullName = "Out RB", Position = "RB", InjuryStatus = "Out" }
            ));

        List<InjuryAlertDocument> captured = [];
        _repo.Setup(r => r.UpsertBatchAsync(It.IsAny<IEnumerable<InjuryAlertDocument>>(), It.IsAny<CancellationToken>()))
             .Callback<IEnumerable<InjuryAlertDocument>, CancellationToken>((docs, _) => captured = [.. docs])
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.DeleteAllAsync(It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        await CreateSut().RunAsync();

        captured.Should().HaveCount(2);
        captured.Should().NotContain(a => a.SleeperPlayerId == "p1");
        captured.Should().Contain(a => a.SleeperPlayerId == "p2");
        captured.Should().Contain(a => a.SleeperPlayerId == "p3");
    }

    [Fact]
    public async Task RunAsync_MapsDesignation_Correctly()
    {
        _sleeperClient
            .Setup(c => c.GetAllPlayersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePlayers(
                new SleeperPlayerDto { PlayerId = "p1", FullName = "IR Player", Position = "QB", InjuryStatus = "IR" },
                new SleeperPlayerDto { PlayerId = "p2", FullName = "Doubtful TE", Position = "TE", InjuryStatus = "D" }
            ));

        List<InjuryAlertDocument> captured = [];
        _repo.Setup(r => r.UpsertBatchAsync(It.IsAny<IEnumerable<InjuryAlertDocument>>(), It.IsAny<CancellationToken>()))
             .Callback<IEnumerable<InjuryAlertDocument>, CancellationToken>((docs, _) => captured = [.. docs])
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.DeleteAllAsync(It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        await CreateSut().RunAsync();

        captured.First(a => a.SleeperPlayerId == "p1").Designation.Should().Be("IR");
        captured.First(a => a.SleeperPlayerId == "p2").Designation.Should().Be("Doubtful");
    }

    [Fact]
    public async Task RunAsync_DeletesAll_BeforeUpsert()
    {
        var callOrder = new List<string>();

        _sleeperClient
            .Setup(c => c.GetAllPlayersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePlayers(
                new SleeperPlayerDto { PlayerId = "p1", FullName = "Q Player", Position = "RB", InjuryStatus = "Q" }
            ));

        _repo.Setup(r => r.DeleteAllAsync(It.IsAny<CancellationToken>()))
             .Callback(() => callOrder.Add("delete"))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.UpsertBatchAsync(It.IsAny<IEnumerable<InjuryAlertDocument>>(), It.IsAny<CancellationToken>()))
             .Callback<IEnumerable<InjuryAlertDocument>, CancellationToken>((_, __) => callOrder.Add("upsert"))
             .Returns(Task.CompletedTask);

        await CreateSut().RunAsync();

        callOrder.Should().Equal("delete", "upsert");
    }

    [Fact]
    public async Task RunAsync_WhenNoInjuredPlayers_UpsertsBatchEmpty()
    {
        _sleeperClient
            .Setup(c => c.GetAllPlayersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakePlayers(
                new SleeperPlayerDto { PlayerId = "p1", FullName = "Healthy", Position = "QB", InjuryStatus = null }
            ));

        _repo.Setup(r => r.DeleteAllAsync(It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);
        _repo.Setup(r => r.UpsertBatchAsync(It.IsAny<IEnumerable<InjuryAlertDocument>>(), It.IsAny<CancellationToken>()))
             .Returns(Task.CompletedTask);

        await CreateSut().RunAsync();

        _repo.Verify(r => r.DeleteAllAsync(It.IsAny<CancellationToken>()), Times.Once);
        _repo.Verify(r => r.UpsertBatchAsync(
            It.Is<IEnumerable<InjuryAlertDocument>>(docs => !docs.Any()),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
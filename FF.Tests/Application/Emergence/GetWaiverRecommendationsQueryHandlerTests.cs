// FF.Tests/Application/Emergence/GetWaiverRecommendationsQueryHandlerTests.cs
using FF.Application.Features.WaiverRecommendations.Queries.GetWaiverRecommendations;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FluentAssertions;
using Moq;

namespace FF.Tests.Application.Emergence;

/// <summary>
/// FAN-118 — this handler no longer computes anything.
///
/// It previously derived replacement levels from a hardcoded slot table, scored
/// players off the zero-PPR column, and wrote the results to Mongo on every read.
/// That work moved to CalculateVorpCommand, where it can be league-aware and where a
/// write is expected. The tests here cover what is left: reading the stored board,
/// and showing only players you could actually add.
/// </summary>
public class GetWaiverRecommendationsQueryHandlerTests
{
    private readonly Mock<IVorpRecommendationRepository> _vorpRepo = new();
    private readonly Mock<ICacheService> _cache = new();

    private GetWaiverRecommendationsQueryHandler CreateSut() =>
        new(_vorpRepo.Object, _cache.Object);

    private static VorpRecommendationDocument Rec(
        string playerId, string position, decimal vorp, bool rostered) =>
        new()
        {
            SleeperLeagueId = "league1",
            PlayerId        = playerId,
            PlayerName      = $"Player {playerId}",
            Position        = position,
            Season          = 2026,
            Week            = 5,
            IsRostered      = rostered,
            ProjectedPoints = 10m + vorp,
            ReplacementLevel = 10m,
            Vorp            = vorp,
            VorpFreeAgent   = vorp
        };

    private void StoredBoard(params VorpRecommendationDocument[] docs) =>
        _vorpRepo
            .Setup(r => r.GetByWeekAsync(
                "league1", 2026, 5, It.IsAny<string?>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(docs);

    [Fact]
    public async Task Handle_ReturnsEmpty_WhenBoardHasNotBeenComputed()
    {
        StoredBoard();

        var result = await CreateSut().Handle(
            new GetWaiverRecommendationsQuery("league1", 2026, 5), CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ExcludesRosteredPlayers()
    {
        StoredBoard(
            Rec("p1", "RB", 8m, rostered: true),
            Rec("p2", "RB", 6m, rostered: false),
            Rec("p3", "WR", 4m, rostered: false));

        var result = await CreateSut().Handle(
            new GetWaiverRecommendationsQuery("league1", 2026, 5), CancellationToken.None);

        result.Select(r => r.PlayerId).Should().BeEquivalentTo(["p2", "p3"]);
    }

    [Fact]
    public async Task Handle_OverFetches_SoTheRosteredFilterCannotStarveTheResult()
    {
        // The repository applies its own Take(top) before this handler filters, so
        // asking for exactly `top` would return fewer than `top` free agents whenever
        // the best players are rostered — which is the normal case.
        StoredBoard();

        await CreateSut().Handle(
            new GetWaiverRecommendationsQuery("league1", 2026, 5, null, 30),
            CancellationToken.None);

        _vorpRepo.Verify(r => r.GetByWeekAsync(
            "league1", 2026, 5, null, 120, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_RespectsTop_AfterFiltering()
    {
        StoredBoard(
            Rec("p1", "RB", 9m, rostered: false),
            Rec("p2", "RB", 8m, rostered: false),
            Rec("p3", "WR", 7m, rostered: false));

        var result = await CreateSut().Handle(
            new GetWaiverRecommendationsQuery("league1", 2026, 5, null, 2),
            CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(r => r.PlayerId).Should().BeEquivalentTo(["p1", "p2"]);
    }

    [Fact]
    public async Task Handle_ReturnsCachedResult_WithoutHittingTheRepository()
    {
        IReadOnlyList<VorpRecommendationDocument> cached = [Rec("cached", "RB", 5m, false)];

        _cache
            .Setup(c => c.Get<IReadOnlyList<VorpRecommendationDocument>>(It.IsAny<string>()))
            .Returns(cached);

        var result = await CreateSut().Handle(
            new GetWaiverRecommendationsQuery("league1", 2026, 5), CancellationToken.None);

        result.Should().BeSameAs(cached);
        _vorpRepo.Verify(r => r.GetByWeekAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<string?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_DoesNotWrite_TheQueryIsReadOnlyNow()
    {
        StoredBoard(Rec("p1", "RB", 5m, rostered: false));

        await CreateSut().Handle(
            new GetWaiverRecommendationsQuery("league1", 2026, 5), CancellationToken.None);

        _vorpRepo.Verify(r => r.UpsertBatchAsync(
            It.IsAny<IEnumerable<VorpRecommendationDocument>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_PassesPositionFilterThrough()
    {
        StoredBoard();

        await CreateSut().Handle(
            new GetWaiverRecommendationsQuery("league1", 2026, 5, "WR"), CancellationToken.None);

        _vorpRepo.Verify(r => r.GetByWeekAsync(
            "league1", 2026, 5, "WR", It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}

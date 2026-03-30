// FF.Tests/Application/Emergence/GetWaiverRecommendationsQueryHandlerTests.cs
using FF.Application.Features.WaiverRecommendations.Queries;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FluentAssertions;
using Moq;

namespace FF.Tests.Application.Emergence;

public class GetWaiverRecommendationsQueryHandlerTests
{
    private readonly Mock<IPlayerProjectionRepository> _projectionRepo = new();
    private readonly Mock<IRosterPlayerRepository> _rosterRepo = new();
    private readonly Mock<IVorpRecommendationRepository> _vorpRepo = new();
    private readonly Mock<ISimulationResultRepository> _simRepo = new();
    private readonly Mock<ICacheService> _cache = new();

    private GetWaiverRecommendationsQueryHandler CreateSut() =>
        new(_projectionRepo.Object, _rosterRepo.Object,
            _vorpRepo.Object, _simRepo.Object, _cache.Object);

    private static PlayerProjectionDocument MakeProjection(
        string playerId, string name, string position, string team, decimal points) =>
        new()
        {
            PlayerId = playerId,
            SleeperPlayerId = playerId,
            PlayerName = name,
            Position = position,
            NflTeam = team,
            Season = 2026,
            Week = 5,
            ProjectedPoints = points
        };

    private static RosterPlayerDocument MakeRoster(
        string leagueId, params string[] playerIds) =>
        new()
        {
            SleeperLeagueId = leagueId,
            SleeperRosterId = Guid.NewGuid().ToString(),
            PlayerIds = [.. playerIds]
        };

    // ── No projections returns empty ──────────────────────────────────────

    [Fact]
    public async Task Handle_ReturnsEmpty_WhenNoProjectionsExist()
    {
        _projectionRepo
            .Setup(r => r.GetByWeekAsync(2026, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _vorpRepo
            .Setup(r => r.GetByWeekAsync(2026, 5, null, 30, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateSut().Handle(
            new GetWaiverRecommendationsQuery("league1", 2026, 5), CancellationToken.None);

        result.Should().BeEmpty();
        _rosterRepo.Verify(
            r => r.GetByLeagueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Rostered players are excluded ────────────────────────────────────

    [Fact]
    public async Task Handle_ExcludesRosteredPlayers()
    {
        var projections = new List<PlayerProjectionDocument>
        {
            MakeProjection("p1", "Rostered RB",   "RB", "KC",  15m),
            MakeProjection("p2", "Available RB",  "RB", "SF",  12m),
            MakeProjection("p3", "Available WR",  "WR", "DAL", 10m)
        };

        _projectionRepo
            .Setup(r => r.GetByWeekAsync(2026, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projections);

        _rosterRepo
            .Setup(r => r.GetByLeagueAsync("league1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([MakeRoster("league1", "p1")]);

        _simRepo
            .Setup(r => r.GetMostRecentBySleeperIdAsync(
                It.IsAny<string>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SimulationResultDocument?)null);

        List<VorpRecommendationDocument> capturedRecs = [];
        _vorpRepo
            .Setup(r => r.UpsertBatchAsync(
                It.IsAny<IEnumerable<VorpRecommendationDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<VorpRecommendationDocument>, CancellationToken>(
                (recs, _) => capturedRecs = [.. recs])
            .Returns(Task.CompletedTask);

        _vorpRepo
            .Setup(r => r.GetByWeekAsync(2026, 5, null, 30, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await CreateSut().Handle(
            new GetWaiverRecommendationsQuery("league1", 2026, 5), CancellationToken.None);

        capturedRecs.Should().NotContain(r => r.PlayerId == "p1");
        capturedRecs.Should().Contain(r => r.PlayerId == "p2");
        capturedRecs.Should().Contain(r => r.PlayerId == "p3");
    }

    // ── VORP is projected points minus replacement level ─────────────────

    [Fact]
    public async Task Handle_CalculatesVorp_AsProjectedMinusReplacementLevel()
    {
        // 25 RBs — replacement level is the 24th ranked player
        var projections = Enumerable.Range(1, 25)
            .Select(i => MakeProjection(
                $"rb{i}", $"RB Player {i}", "RB", "KC",
                (26m - i)))   // rb1=25, rb2=24 ... rb24=2, rb25=1
            .ToList();

        _projectionRepo
            .Setup(r => r.GetByWeekAsync(2026, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projections);

        _rosterRepo
            .Setup(r => r.GetByLeagueAsync("league1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _simRepo
            .Setup(r => r.GetMostRecentBySleeperIdAsync(
                It.IsAny<string>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SimulationResultDocument?)null);

        List<VorpRecommendationDocument> capturedRecs = [];
        _vorpRepo
            .Setup(r => r.UpsertBatchAsync(
                It.IsAny<IEnumerable<VorpRecommendationDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<VorpRecommendationDocument>, CancellationToken>(
                (recs, _) => capturedRecs = [.. recs])
            .Returns(Task.CompletedTask);

        _vorpRepo
            .Setup(r => r.GetByWeekAsync(2026, 5, null, 30, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await CreateSut().Handle(
            new GetWaiverRecommendationsQuery("league1", 2026, 5), CancellationToken.None);

        // Replacement level = 24th player = 26 - 24 = 2 points
        // Top player (rb1 = 25 pts) VORP = 25 - 2 = 23
        var topRb = capturedRecs.First(r => r.PlayerId == "rb1");
        topRb.ReplacementLevel.Should().Be(2m);
        topRb.Vorp.Should().Be(23m);
    }

    // ── Floor and ceiling populated from simulation results ───────────────

    [Fact]
    public async Task Handle_PopulatesFloorAndCeiling_FromSimulationResults()
    {
        var projections = new List<PlayerProjectionDocument>
        {
            MakeProjection("p1", "WR One", "WR", "BUF", 14m)
        };

        _projectionRepo
            .Setup(r => r.GetByWeekAsync(2026, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projections);

        _rosterRepo
            .Setup(r => r.GetByLeagueAsync("league1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _simRepo
            .Setup(r => r.GetMostRecentBySleeperIdAsync(
                "p1", 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SimulationResultDocument
            {
                PlayerId = "p1",
                Floor = 6m,
                Ceiling = 28m,
                Median = 14m
            });

        List<VorpRecommendationDocument> capturedRecs = [];
        _vorpRepo
            .Setup(r => r.UpsertBatchAsync(
                It.IsAny<IEnumerable<VorpRecommendationDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<VorpRecommendationDocument>, CancellationToken>(
                (recs, _) => capturedRecs = [.. recs])
            .Returns(Task.CompletedTask);

        _vorpRepo
            .Setup(r => r.GetByWeekAsync(2026, 5, null, 30, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await CreateSut().Handle(
            new GetWaiverRecommendationsQuery("league1", 2026, 5), CancellationToken.None);

        var rec = capturedRecs.Should().ContainSingle().Subject;
        rec.FloorPoints.Should().Be(6m);
        rec.CeilingPoints.Should().Be(28m);
    }

    // ── VORP rank is assigned across all positions ────────────────────────

    [Fact]
    public async Task Handle_AssignsVorpRank_AcrossAllPositions()
    {
        var projections = new List<PlayerProjectionDocument>
        {
            MakeProjection("qb1", "Elite QB",  "QB", "KC",  28m),
            MakeProjection("wr1", "Good WR",   "WR", "BUF", 16m),
            MakeProjection("rb1", "Average RB", "RB", "SF", 10m)
        };

        _projectionRepo
            .Setup(r => r.GetByWeekAsync(2026, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projections);

        _rosterRepo
            .Setup(r => r.GetByLeagueAsync("league1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _simRepo
            .Setup(r => r.GetMostRecentBySleeperIdAsync(
                It.IsAny<string>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SimulationResultDocument?)null);

        List<VorpRecommendationDocument> capturedRecs = [];
        _vorpRepo
            .Setup(r => r.UpsertBatchAsync(
                It.IsAny<IEnumerable<VorpRecommendationDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<VorpRecommendationDocument>, CancellationToken>(
                (recs, _) => capturedRecs = [.. recs])
            .Returns(Task.CompletedTask);

        _vorpRepo
            .Setup(r => r.GetByWeekAsync(2026, 5, null, 30, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await CreateSut().Handle(
            new GetWaiverRecommendationsQuery("league1", 2026, 5), CancellationToken.None);

        var ranks = capturedRecs.Select(r => r.VorpRank).OrderBy(r => r).ToList();
        ranks.Should().BeEquivalentTo([1, 2, 3]);
        capturedRecs.Should().OnlyHaveUniqueItems(r => r.VorpRank);
    }

    // ── Position rank is per-position ────────────────────────────────────

    [Fact]
    public async Task Handle_AssignsPositionRank_WithinPosition()
    {
        var projections = new List<PlayerProjectionDocument>
        {
            MakeProjection("wr1", "WR One",   "WR", "KC",  18m),
            MakeProjection("wr2", "WR Two",   "WR", "BUF", 14m),
            MakeProjection("wr3", "WR Three", "WR", "DAL", 10m)
        };

        _projectionRepo
            .Setup(r => r.GetByWeekAsync(2026, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(projections);

        _rosterRepo
            .Setup(r => r.GetByLeagueAsync("league1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _simRepo
            .Setup(r => r.GetMostRecentBySleeperIdAsync(
                It.IsAny<string>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync((SimulationResultDocument?)null);

        List<VorpRecommendationDocument> capturedRecs = [];
        _vorpRepo
            .Setup(r => r.UpsertBatchAsync(
                It.IsAny<IEnumerable<VorpRecommendationDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<VorpRecommendationDocument>, CancellationToken>(
                (recs, _) => capturedRecs = [.. recs])
            .Returns(Task.CompletedTask);

        _vorpRepo
            .Setup(r => r.GetByWeekAsync(2026, 5, null, 30, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        await CreateSut().Handle(
            new GetWaiverRecommendationsQuery("league1", 2026, 5), CancellationToken.None);

        var wrRecs = capturedRecs
            .Where(r => r.Position == "WR")
            .OrderBy(r => r.PositionRank)
            .ToList();

        wrRecs.Should().HaveCount(3);
        wrRecs[0].PlayerId.Should().Be("wr1");
        wrRecs[0].PositionRank.Should().Be(1);
        wrRecs[1].PlayerId.Should().Be("wr2");
        wrRecs[1].PositionRank.Should().Be(2);
        wrRecs[2].PlayerId.Should().Be("wr3");
        wrRecs[2].PositionRank.Should().Be(3);
    }
}
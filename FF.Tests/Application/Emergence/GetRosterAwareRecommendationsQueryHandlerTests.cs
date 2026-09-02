// FF.Tests/Application/Emergence/GetRosterAwareRecommendationsQueryHandlerTests.cs
using FF.Application.Features.RosterAwareRecommendations.Queries;
using FF.Application.Interfaces.Persistence;
using FF.Application.Services;
using FF.Domain.Documents;
using FluentAssertions;
using Moq;

namespace FF.Tests.Application.Emergence;

public class GetRosterAwareRecommendationsQueryHandlerTests
{
    private readonly Mock<IVorpRecommendationRepository> _vorpRepo = new();
    private readonly Mock<IRosterPlayerRepository> _rosterRepo = new();
    private readonly Mock<ISimulationResultRepository> _simRepo = new();

    private GetRosterAwareRecommendationsQueryHandler CreateSut()
    {
        var profileService = new RosterProfileService(_rosterRepo.Object, _simRepo.Object);
        return new GetRosterAwareRecommendationsQueryHandler(_vorpRepo.Object, profileService);
    }

    private static VorpRecommendationDocument MakeVorp(
        string playerId, string name, string position, decimal vorp) =>
        new()
        {
            // FAN-118: the board is league-scoped, and it now stores rostered players
            // too. IsRostered stays false here — these fixtures are waiver candidates.
            SleeperLeagueId = "league1",
            PlayerId = playerId,
            PlayerName = name,
            Position = position,
            NflTeam = "KC",
            Season = 2026,
            Week = 5,
            IsRostered = false,
            ProjectedPoints = vorp + 5m,
            Vorp = vorp,
            VorpRank = 1
        };

    private static RosterPlayerDocument MakeRoster(
        string leagueId, string sleeperUserId, params string[] playerIds) =>
        new()
        {
            SleeperLeagueId = leagueId,
            SleeperRosterId = Guid.NewGuid().ToString(),
            SleeperUserId = sleeperUserId,
            PlayerIds = [.. playerIds]
        };

    // ── Falls back to plain VORP order when roster not found ─────────────

    [Fact]
    public async Task Handle_FallsBackToVorpOrder_WhenRosterNotFound()
    {
        var vorpRecs = new List<VorpRecommendationDocument>
        {
            MakeVorp("wr1", "WR One", "WR", 8m),
            MakeVorp("rb1", "RB One", "RB", 6m)
        };

        _vorpRepo
            .Setup(r => r.GetByWeekAsync("league1", 2026, 5, null, 180, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vorpRecs);

        // No roster found for this user
        _rosterRepo
            .Setup(r => r.GetByLeagueAsync("league1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateSut().Handle(
            new GetRosterAwareRecommendationsQuery("league1", "user1", 2026, 5),
            CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Base.PlayerId.Should().Be("wr1");
        result[0].PositionNeed.Should().Be(RosterNeed.Neutral);
        result.Should().AllSatisfy(r => r.FitRank.Should().BeGreaterThan(0));
    }

    // ── Need position gets boosted ────────────────────────────────────────

    [Fact]
    public async Task Handle_BoostsNeedPosition_WhenRosterIsThin()
    {
        // Two WRs with equal VORP — one RB with same VORP
        // User has 0 RBs on roster → RB is a Need
        var vorpRecs = new List<VorpRecommendationDocument>
        {
            MakeVorp("wr1", "WR One", "WR", 10m),
            MakeVorp("rb1", "RB One", "RB", 10m)
        };

        _vorpRepo
            .Setup(r => r.GetByWeekAsync("league1", 2026, 5, null, 180, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vorpRecs);

        // User roster has 5 WRs (Strength) but 0 RBs (Need)
        var userRoster = MakeRoster("league1", "user1",
            "wr_a", "wr_b", "wr_c", "wr_d", "wr_e", "wr_f");  // 6 WRs = Strength

        _rosterRepo
            .Setup(r => r.GetByLeagueAsync("league1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([userRoster]);

        // Sim results for rostered WRs
        _simRepo
            .Setup(r => r.GetMostRecentBySleeperIdAsync(
                It.IsAny<string>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SimulationResultDocument
            {
                Position = "WR",
                Floor = 5m,
                Ceiling = 20m,
                Median = 12m
            });

        var result = await CreateSut().Handle(
            new GetRosterAwareRecommendationsQuery("league1", "user1", 2026, 5),
            CancellationToken.None);

        // RB should rank first — same VORP but boosted by Need multiplier (1.30)
        result.Should().HaveCount(2);
        result[0].Base.Position.Should().Be("RB");
        result[0].PositionNeed.Should().Be(RosterNeed.Need);
        result[0].FitScore.Should().Be(13m);  // 10 * 1.30

        result[1].Base.Position.Should().Be("WR");
        result[1].PositionNeed.Should().Be(RosterNeed.Strength);
        result[1].FitScore.Should().Be(7.5m); // 10 * 0.75
    }

    // ── Strength position gets discounted ─────────────────────────────────

    [Fact]
    public async Task Handle_DiscounteStrengthPosition_WhenRosterIsDeep()
    {
        var vorpRecs = new List<VorpRecommendationDocument>
        {
            MakeVorp("wr1", "WR One", "WR", 12m)
        };

        _vorpRepo
            .Setup(r => r.GetByWeekAsync("league1", 2026, 5, null, 180, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vorpRecs);

        // User has 6 WRs — above depth target of 5 = Strength
        var userRoster = MakeRoster("league1", "user1",
            "wr_a", "wr_b", "wr_c", "wr_d", "wr_e", "wr_f");

        _rosterRepo
            .Setup(r => r.GetByLeagueAsync("league1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([userRoster]);

        _simRepo
            .Setup(r => r.GetMostRecentBySleeperIdAsync(
                It.IsAny<string>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SimulationResultDocument
            {
                Position = "WR",
                Floor = 5m,
                Ceiling = 20m,
                Median = 12m
            });

        var result = await CreateSut().Handle(
            new GetRosterAwareRecommendationsQuery("league1", "user1", 2026, 5),
            CancellationToken.None);

        result.Should().ContainSingle();
        result[0].PositionNeed.Should().Be(RosterNeed.Strength);
        result[0].FitScore.Should().Be(9m);  // 12 * 0.75
    }

    // ── Neutral position unchanged ────────────────────────────────────────

    [Fact]
    public async Task Handle_NeutralPosition_LeavesVorpUnchanged()
    {
        var vorpRecs = new List<VorpRecommendationDocument>
        {
            MakeVorp("te1", "TE One", "TE", 8m)
        };

        _vorpRepo
            .Setup(r => r.GetByWeekAsync("league1", 2026, 5, null, 180, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vorpRecs);

        // User has exactly 2 TEs — depth target = 2 = Neutral
        var userRoster = MakeRoster("league1", "user1", "te_a", "te_b");

        _rosterRepo
            .Setup(r => r.GetByLeagueAsync("league1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([userRoster]);

        _simRepo
            .Setup(r => r.GetMostRecentBySleeperIdAsync(
                It.IsAny<string>(), 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SimulationResultDocument
            {
                Position = "TE",
                Floor = 3m,
                Ceiling = 15m,
                Median = 8m
            });

        var result = await CreateSut().Handle(
            new GetRosterAwareRecommendationsQuery("league1", "user1", 2026, 5),
            CancellationToken.None);

        result.Should().ContainSingle();
        result[0].PositionNeed.Should().Be(RosterNeed.Neutral);
        result[0].FitScore.Should().Be(8m);  // 8 * 1.00 — unchanged
    }

    // ── FitRank is sequential with no gaps ───────────────────────────────

    [Fact]
    public async Task Handle_FitRanks_AreSequentialStartingAt1()
    {
        var vorpRecs = new List<VorpRecommendationDocument>
        {
            MakeVorp("p1", "Player 1", "WR", 10m),
            MakeVorp("p2", "Player 2", "RB", 8m),
            MakeVorp("p3", "Player 3", "TE", 6m),
            MakeVorp("p4", "Player 4", "QB", 4m)
        };

        _vorpRepo
            .Setup(r => r.GetByWeekAsync("league1", 2026, 5, null, 180, It.IsAny<CancellationToken>()))
            .ReturnsAsync(vorpRecs);

        _rosterRepo
            .Setup(r => r.GetByLeagueAsync("league1", It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateSut().Handle(
            new GetRosterAwareRecommendationsQuery("league1", "user1", 2026, 5),
            CancellationToken.None);

        var ranks = result.Select(r => r.FitRank).OrderBy(r => r).ToList();
        ranks.Should().BeEquivalentTo([1, 2, 3, 4]);
        result.Should().OnlyHaveUniqueItems(r => r.FitRank);
    }

    // ── Empty VORP returns empty result ───────────────────────────────────

    [Fact]
    public async Task Handle_ReturnsEmpty_WhenNoVorpRecsExist()
    {
        _vorpRepo
            .Setup(r => r.GetByWeekAsync("league1", 2026, 5, null, 180, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await CreateSut().Handle(
            new GetRosterAwareRecommendationsQuery("league1", "user1", 2026, 5),
            CancellationToken.None);

        result.Should().BeEmpty();
        _rosterRepo.Verify(
            r => r.GetByLeagueAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
// FF.Tests/Application/Simulations/SeedSeasonAverageShrinkageTests.cs
//
// 2026-09-07. Pins the small-sample shrinkage added to the season-average seed.
//
// The bug these exist for: the seed divided season points by games with `games > 0`
// as the only gate, so a two-game sample produced a season-long per-game rate that
// outranked players with a full year of evidence. Measured case — Joe Milton's 2024
// row seeded at 19.24 half-PPR per game, a top-five quarterback rate built on one
// afternoon, which is where "why is Joe Milton on my dynasty board" came from.
//
// Like the sibling matching tests, these drive the handler through CsvContent, so
// no HTTP is involved.

using FF.Application.Features.Simulations.Commands.SeedSeasonAverageSims;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Domain.Entities;
using FF.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;

namespace FF.Tests.Application.Simulations;

public class SeedSeasonAverageShrinkageTests
{
    private readonly Mock<ISimulationResultRepository> _simRepo = new();
    private readonly Mock<IPlayerRepository> _playerRepo = new();
    private readonly Mock<IPlayerIdResolutionService> _resolution = new();
    private readonly Mock<IHttpClientFactory> _httpFactory = new();
    private readonly Mock<ILogger<SeedSeasonAverageSimsCommandHandler>> _logger = new();

    private List<SimulationResultDocument> _upserted = [];

    private SeedSeasonAverageSimsCommandHandler CreateHandler()
    {
        _simRepo
            .Setup(r => r.UpsertBatchAsync(
                It.IsAny<IEnumerable<SimulationResultDocument>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<SimulationResultDocument>, CancellationToken>(
                (docs, _) => _upserted = docs.ToList())
            .Returns(Task.CompletedTask);

        return new SeedSeasonAverageSimsCommandHandler(
            _simRepo.Object, _playerRepo.Object, _resolution.Object,
            _httpFactory.Object, _logger.Object);
    }

    private static Player MakePlayer(string sleeperId, string first, string last, Position pos)
        => Player.Create(first, last, pos, "SEA", sleeperId, null, null);

    private void SetupPlayers(params Player[] players) =>
        _playerRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(players.ToList());

    private void SetupNoGsisBridge() =>
        _resolution
            .Setup(r => r.BuildGsisToSleeperMapAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());

    private SimulationResultDocument Doc(string sleeperId) =>
        _upserted.Single(d => d.SleeperPlayerId == sleeperId);

    // Three running backs, chosen so the positional prior is arithmetically obvious.
    //
    //   Aaron  — 17 games, 170.0 pts, 0 rec → 10.0 per game
    //   Bruce  — 17 games, 102.0 pts, 0 rec →  6.0 per game
    //   Carl   —  2 games,  40.0 pts, 0 rec → 20.0 per game   ← the Milton shape
    //
    // Median of {6, 10, 20} is 10.0, so the prior is exactly 10.0.
    private const string ThreeBacksCsv =
        "player_id,player_display_name,position,recent_team,season_type,games,fantasy_points,receptions\n" +
        "00-0000001,Aaron Alpha,RB,SEA,REG,17,170.0,0\n" +
        "00-0000002,Bruce Bravo,RB,SEA,REG,17,102.0,0\n" +
        "00-0000003,Carl Charlie,RB,SEA,REG,2,40.0,0\n";

    private void SetupThreeBacks() => SetupPlayers(
        MakePlayer("1001", "Aaron", "Alpha", Position.RB),
        MakePlayer("1002", "Bruce", "Bravo", Position.RB),
        MakePlayer("1003", "Carl", "Charlie", Position.RB));

    [Fact]
    public async Task SmallSample_IsShrunkTowardThePositionalPrior()
    {
        SetupThreeBacks();
        SetupNoGsisBridge();

        await CreateHandler().Handle(
            new SeedSeasonAverageSimsCommand(2025, ThreeBacksCsv), CancellationToken.None);

        // weight = 2/8 = 0.25 → 0.25 * 20.0 + 0.75 * 10.0 = 12.5
        Doc("1003").Median.Should().Be(12.5m);
    }

    [Fact]
    public async Task SmallSample_LandsBelowItsOwnRawRate_AndAboveThePrior()
    {
        SetupThreeBacks();
        SetupNoGsisBridge();

        await CreateHandler().Handle(
            new SeedSeasonAverageSimsCommand(2025, ThreeBacksCsv), CancellationToken.None);

        // The point of shrinkage rather than a cutoff: he keeps some of his own
        // signal (he did play well) without keeping all of it (he barely played).
        Doc("1003").Median.Should().BeLessThan(20.0m);
        Doc("1003").Median.Should().BeGreaterThan(10.0m);
    }

    [Fact]
    public async Task FullSample_IsUntouched()
    {
        SetupThreeBacks();
        SetupNoGsisBridge();

        await CreateHandler().Handle(
            new SeedSeasonAverageSimsCommand(2025, ThreeBacksCsv), CancellationToken.None);

        // 17 games is well past the threshold, so these must pass through exactly
        // as before — shrinkage is not allowed to move the players it is not for.
        Doc("1001").Median.Should().Be(10.0m);
        Doc("1002").Median.Should().Be(6.0m);
    }

    [Fact]
    public async Task EveryDistributionFieldMovesWithTheShrunkAverage()
    {
        SetupThreeBacks();
        SetupNoGsisBridge();

        await CreateHandler().Handle(
            new SeedSeasonAverageSimsCommand(2025, ThreeBacksCsv), CancellationToken.None);

        var carl = Doc("1003");

        // Mean and BaseProjection are the same number as Median on a season-average
        // row; Floor and Ceiling are fixed multiples of it. If shrinkage were applied
        // to only one of them the row would be internally inconsistent — which is the
        // exact class of bug that produced the mean-vs-median mismatch on 09-06.
        carl.Mean.Should().Be(12.5m);
        carl.BaseProjection.Should().Be(12.5m);
        carl.Floor.Should().Be(7.5m);      // 12.5 * 0.6
        carl.Ceiling.Should().Be(18.75m);  // 12.5 * 1.5
    }

    [Fact]
    public async Task GameSampleSize_IsStamped_SoConsumersCanWeightOrFilter()
    {
        SetupThreeBacks();
        SetupNoGsisBridge();

        await CreateHandler().Handle(
            new SeedSeasonAverageSimsCommand(2025, ThreeBacksCsv), CancellationToken.None);

        Doc("1003").GameSampleSize.Should().Be(2);
        Doc("1001").GameSampleSize.Should().Be(17);
    }

    [Fact]
    public async Task ShrinkageIsPerPosition_NotGlobal()
    {
        // A quarterback's per-game rate is roughly double a running back's. Pooling
        // them into one prior would drag every small-sample RB upward and every
        // small-sample QB downward.
        const string csv =
            "player_id,player_display_name,position,recent_team,season_type,games,fantasy_points,receptions\n" +
            "00-0000001,Aaron Alpha,RB,SEA,REG,17,170.0,0\n" +
            "00-0000002,Bruce Bravo,RB,SEA,REG,17,102.0,0\n" +
            "00-0000003,Carl Charlie,RB,SEA,REG,2,40.0,0\n" +
            "00-0000004,Dave Delta,QB,SEA,REG,17,340.0,0\n" +
            "00-0000005,Earl Echo,QB,SEA,REG,17,306.0,0\n" +
            "00-0000006,Frank Foxtrot,QB,SEA,REG,2,40.0,0\n";

        SetupPlayers(
            MakePlayer("1001", "Aaron", "Alpha", Position.RB),
            MakePlayer("1002", "Bruce", "Bravo", Position.RB),
            MakePlayer("1003", "Carl", "Charlie", Position.RB),
            MakePlayer("1004", "Dave", "Delta", Position.QB),
            MakePlayer("1005", "Earl", "Echo", Position.QB),
            MakePlayer("1006", "Frank", "Foxtrot", Position.QB));
        SetupNoGsisBridge();

        await CreateHandler().Handle(
            new SeedSeasonAverageSimsCommand(2025, csv), CancellationToken.None);

        // RB prior = median{10, 6, 20} = 10 → 0.25 * 20 + 0.75 * 10 = 12.5
        Doc("1003").Median.Should().Be(12.5m);

        // QB prior = median{20, 18, 20} = 20 → 0.25 * 20 + 0.75 * 20 = 20.0
        // Same raw rate as Carl, very different answer, because the position it is
        // measured against is different.
        Doc("1006").Median.Should().Be(20.0m);
    }
}

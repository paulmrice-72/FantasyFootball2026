// FF.Tests/Application/Simulations/SeedSeasonAverageSimsMatchingTests.cs
//
// FAN-131. These pin the identity-resolution behaviour of the season-average
// seed. They use the command's CsvContent parameter, so no HTTP is involved and
// the nflverse download path is never exercised.
//
// The scenario is the real one from 2026-09-02: the Players table holds two rows
// normalising to "kenneth walker" — 8151 (RB, the starter) and 4634 (WR) — and
// the stat row belongs to the running back.

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

public class SeedSeasonAverageSimsMatchingTests
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

    private static Player MakePlayer(
        string sleeperId, string first, string last, Position pos,
        string? nflTeam = null, string? gsisId = null)
        => Player.Create(first, last, pos, nflTeam, sleeperId, gsisId, null);

    private void SetupPlayers(params Player[] players) =>
        _playerRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(players.ToList());

    private void SetupGsisBridge(Dictionary<string, string> map) =>
        _resolution
            .Setup(r => r.BuildGsisToSleeperMapAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(map);

    // Season-aggregate shape (no "week" column), so AggregateWeeklyToSeason is skipped.
    private const string WalkerCsv =
        "player_id,player_display_name,position,recent_team,season_type,games,fantasy_points,receptions\n" +
        "00-0038134,Kenneth Walker III,RB,SEA,REG,15,180.5,30\n";

    // ── The bug ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task NameCollision_ResolvesByPosition_NotByArbitraryFirst()
    {
        // Both normalise to "kenneth walker". Only one is a running back.
        SetupPlayers(
            MakePlayer("4634", "Kenneth", "Walker", Position.WR),
            MakePlayer("8151", "Kenneth", "Walker", Position.RB, "SEA"));
        SetupGsisBridge([]);   // bridge dead — forces the name path

        var result = await CreateHandler().Handle(
            new SeedSeasonAverageSimsCommand(2025, WalkerCsv), CancellationToken.None);

        _upserted.Should().ContainSingle();
        _upserted[0].SleeperPlayerId.Should().Be("8151");
        _upserted[0].Position.Should().Be("RB");
        result.MatchedByName.Should().Be(1);
        result.AmbiguousSkipped.Should().Be(0);
    }

    [Fact]
    public async Task NameCollision_SamePosition_IsSkippedNotGuessed()
    {
        // Two running backs with the same normalised name and nothing to separate
        // them. Writing one player's season onto the other is worse than writing
        // nothing, because nothing downstream can tell it from real data.
        SetupPlayers(
            MakePlayer("8151", "Kenneth", "Walker", Position.RB),
            MakePlayer("9999", "Kenneth", "Walker III", Position.RB));
        SetupGsisBridge([]);

        var result = await CreateHandler().Handle(
            new SeedSeasonAverageSimsCommand(2025, WalkerCsv), CancellationToken.None);

        _upserted.Should().BeEmpty();
        result.AmbiguousSkipped.Should().Be(1);
        result.Seeded.Should().Be(0);
    }

    [Fact]
    public async Task NameCollision_BrokenByNflTeam_WhenPositionsMatch()
    {
        SetupPlayers(
            MakePlayer("8151", "Kenneth", "Walker", Position.RB, "SEA"),
            MakePlayer("9999", "Kenneth", "Walker", Position.RB));
        SetupGsisBridge([]);

        await CreateHandler().Handle(
            new SeedSeasonAverageSimsCommand(2025, WalkerCsv), CancellationToken.None);

        _upserted.Should().ContainSingle();
        _upserted[0].SleeperPlayerId.Should().Be("8151");
    }

    // ── The reason the name path was carrying everything ──────────────────────

    [Fact]
    public async Task GsisBridge_WithRStyleFloatId_StillResolves()
    {
        // nflverse roster CSVs come out of R, which writes numeric ids as "8151.0".
        // Unnormalised, this misses every SleeperPlayerId and silently demotes the
        // whole import to name matching.
        SetupPlayers(
            MakePlayer("4634", "Kenneth", "Walker", Position.WR),
            MakePlayer("8151", "Kenneth", "Walker", Position.RB, "SEA"));
        SetupGsisBridge(new Dictionary<string, string> { ["00-0038134"] = "8151.0" });

        var result = await CreateHandler().Handle(
            new SeedSeasonAverageSimsCommand(2025, WalkerCsv), CancellationToken.None);

        _upserted.Should().ContainSingle();
        _upserted[0].SleeperPlayerId.Should().Be("8151");
        result.MatchedByGsis.Should().Be(1);
        result.MatchedByName.Should().Be(0);
    }

    [Fact]
    public async Task StoredGsisId_IsUsedWhenBridgeMisses()
    {
        SetupPlayers(
            MakePlayer("4634", "Kenneth", "Walker", Position.WR),
            MakePlayer("8151", "Kenneth", "Walker", Position.RB, "SEA", "00-0038134"));
        SetupGsisBridge([]);

        var result = await CreateHandler().Handle(
            new SeedSeasonAverageSimsCommand(2025, WalkerCsv), CancellationToken.None);

        _upserted[0].SleeperPlayerId.Should().Be("8151");
        result.MatchedByGsis.Should().Be(1);
    }

    // ── Placeholder rows must not compete ─────────────────────────────────────

    [Fact]
    public async Task PlaceholderPlayerRows_AreExcludedFromTheNameIndex()
    {
        SetupPlayers(
            MakePlayer("12466", "Player", "Invalid", Position.RB),
            MakePlayer("5755", "Duplicate", "Player", Position.RB),
            MakePlayer("8151", "Kenneth", "Walker", Position.RB, "SEA"));
        SetupGsisBridge([]);

        await CreateHandler().Handle(
            new SeedSeasonAverageSimsCommand(2025, WalkerCsv), CancellationToken.None);

        _upserted.Should().ContainSingle();
        _upserted[0].SleeperPlayerId.Should().Be("8151");
    }

    // ── Arithmetic ────────────────────────────────────────────────────────────

    [Fact]
    public async Task HalfPprAverage_IsPointsPlusHalfReceptions_PerGame()
    {
        SetupPlayers(MakePlayer("8151", "Kenneth", "Walker", Position.RB, "SEA"));
        SetupGsisBridge([]);

        await CreateHandler().Handle(
            new SeedSeasonAverageSimsCommand(2025, WalkerCsv), CancellationToken.None);

        // (180.5 + 30 * 0.5) / 15 = 13.033... -> 13.03
        _upserted[0].Median.Should().Be(13.03m);
        _upserted[0].Week.Should().Be(0);
        _upserted[0].Season.Should().Be(2025);
        _upserted[0].PlayerRole.Should().Be("SeasonAverage");
    }

    [Fact]
    public async Task FloatFormattedGamesColumn_IsNotSkipped()
    {
        // The filter parsed games as decimal while the body parsed it as int, so
        // "15.0" passed the filter and was then dropped as unparseable.
        const string csv =
            "player_id,player_display_name,position,recent_team,season_type,games,fantasy_points,receptions\n" +
            "00-0038134,Kenneth Walker III,RB,SEA,REG,15.0,180.5,30\n";

        SetupPlayers(MakePlayer("8151", "Kenneth", "Walker", Position.RB, "SEA"));
        SetupGsisBridge([]);

        var result = await CreateHandler().Handle(
            new SeedSeasonAverageSimsCommand(2025, csv), CancellationToken.None);

        result.Seeded.Should().Be(1);
        result.Skipped.Should().Be(0);
        _upserted[0].Median.Should().Be(13.03m);
    }

    // ── Team column drift ─────────────────────────────────────────────────────

    [Fact]
    public async Task TeamColumn_IsReadUnderEitherSpelling()
    {
        // Prod's 2025 rows all carry NflTeam "" because the seed ran against a file
        // that had renamed recent_team -> team.
        const string csv =
            "player_id,player_display_name,position,team,season_type,games,fantasy_points,receptions\n" +
            "00-0038134,Kenneth Walker III,RB,SEA,REG,15,180.5,30\n";

        SetupPlayers(MakePlayer("8151", "Kenneth", "Walker", Position.RB, "SEA"));
        SetupGsisBridge([]);

        await CreateHandler().Handle(
            new SeedSeasonAverageSimsCommand(2025, csv), CancellationToken.None);

        _upserted[0].NflTeam.Should().Be("SEA");
    }
}

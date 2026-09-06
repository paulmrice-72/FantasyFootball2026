// FF.Tests/Application/FpDynastyImportMatchingTests.cs
//
// 2026-09-07. Pins name resolution in the FantasyPros dynasty import.
//
// Three failure modes, all measured from the 2026-09-06 calibration runs:
//
//   1. Generational suffixes — FP writes "Patrick Mahomes II", Sleeper writes
//      "Patrick Mahomes". Nine of the ten most valuable unmatched players.
//   2. Nicknames — FP publishes "Hollywood Brown" and "Bam Knight" for Marquise
//      Brown and Zonovan Knight. No normalizer turns one into the other.
//   3. Ambiguity — two Kenneth Walkers exist (8151 RB, 4634 WR) and the importer
//      used GroupBy(name).First(), binding whichever Mongo returned first. Same
//      defect FAN-131 fixed in the season-average seed.

using FF.Application.Features.DraftTools.Commands.ImportFantasyProsDynastyRankings;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using FF.Domain.Entities;
using FF.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FF.Tests.Application;

public class FpDynastyImportMatchingTests
{
    private readonly Mock<IFantasyProsRookieRankingRepository> _rankingRepo = new();
    private readonly Mock<IPlayerRepository> _playerRepo = new();

    private List<FantasyProsRookieRankingDocument> _saved = [];

    private ImportFantasyProsDynastyRankingsCommandHandler CreateHandler(params Player[] players)
    {
        _playerRepo
            .Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(players.ToList());

        _rankingRepo
            .Setup(r => r.UpsertManyAsync(
                It.IsAny<IEnumerable<FantasyProsRookieRankingDocument>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<FantasyProsRookieRankingDocument>, CancellationToken>(
                (docs, _) => _saved = docs.ToList())
            .Returns(Task.CompletedTask);

        return new ImportFantasyProsDynastyRankingsCommandHandler(
            _rankingRepo.Object,
            _playerRepo.Object,
            NullLogger<ImportFantasyProsDynastyRankingsCommandHandler>.Instance);
    }

    private static Player MakePlayer(
        string sleeperId, string first, string last, Position pos, string team)
        => Player.Create(first, last, pos, team, sleeperId, null, null);

    private static string Csv(params string[] rows) =>
        "RK,\"PLAYER NAME\",TEAM,POS\n" + string.Join('\n', rows);

    private FantasyProsRookieRankingDocument Doc(string fpName) =>
        _saved.Single(d => d.PlayerName == fpName);

    // ── Suffixes ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerationalSuffix_MatchesTheRosterName()
    {
        var handler = CreateHandler(
            MakePlayer("4046", "Patrick", "Mahomes", Position.QB, "KC"));

        await handler.Handle(new ImportFantasyProsDynastyRankingsCommand(
            Csv("1,\"Patrick Mahomes II\",KC,QB1"), 2026), CancellationToken.None);

        Doc("Patrick Mahomes II").SleeperPlayerId.Should().Be("4046");
    }

    // ── Nicknames ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Nickname_MatchesBySurnamePositionAndTeam()
    {
        var handler = CreateHandler(
            MakePlayer("5000", "Marquise", "Brown", Position.WR, "KC"));

        await handler.Handle(new ImportFantasyProsDynastyRankingsCommand(
            Csv("1,\"Hollywood Brown\",KC,WR1"), 2026), CancellationToken.None);

        Doc("Hollywood Brown").SleeperPlayerId.Should().Be("5000");
    }

    [Fact]
    public async Task Nickname_MatchesForAShortenedFirstName()
    {
        var handler = CreateHandler(
            MakePlayer("6000", "Zonovan", "Knight", Position.RB, "NYJ"));

        await handler.Handle(new ImportFantasyProsDynastyRankingsCommand(
            Csv("1,\"Bam Knight\",NYJ,RB1"), 2026), CancellationToken.None);

        Doc("Bam Knight").SleeperPlayerId.Should().Be("6000");
    }

    [Fact]
    public async Task Nickname_IsRefusedWhenTheSurnameIsNotUniqueOnThatTeam()
    {
        // Two receivers named Brown on the same team: the surname no longer
        // identifies anyone, so the import must decline rather than pick one.
        var handler = CreateHandler(
            MakePlayer("5000", "Marquise", "Brown", Position.WR, "KC"),
            MakePlayer("5001", "Anthony", "Brown", Position.WR, "KC"));

        await handler.Handle(new ImportFantasyProsDynastyRankingsCommand(
            Csv("1,\"Hollywood Brown\",KC,WR1"), 2026), CancellationToken.None);

        Doc("Hollywood Brown").SleeperPlayerId.Should().BeEmpty();
    }

    [Fact]
    public async Task Nickname_IsRefusedWhenTheTeamDoesNotMatch()
    {
        // A surname match with the wrong team is not evidence. Refusing leaves the
        // player unmatched, which is recoverable; a wrong bind is not.
        var handler = CreateHandler(
            MakePlayer("5000", "Marquise", "Brown", Position.WR, "KC"));

        await handler.Handle(new ImportFantasyProsDynastyRankingsCommand(
            Csv("1,\"Hollywood Brown\",BAL,WR1"), 2026), CancellationToken.None);

        Doc("Hollywood Brown").SleeperPlayerId.Should().BeEmpty();
    }

    // ── Ambiguity ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task DuplicateName_ResolvesByPosition_NotByArbitraryFirst()
    {
        // The real pair from FAN-131: 8151 is the running back, 4634 the receiver.
        var handler = CreateHandler(
            MakePlayer("4634", "Kenneth", "Walker", Position.WR, "SEA"),
            MakePlayer("8151", "Kenneth", "Walker", Position.RB, "SEA"));

        await handler.Handle(new ImportFantasyProsDynastyRankingsCommand(
            Csv("1,\"Kenneth Walker III\",SEA,RB1"), 2026), CancellationToken.None);

        Doc("Kenneth Walker III").SleeperPlayerId.Should().Be("8151");
    }

    [Fact]
    public async Task DuplicateNameAndPosition_ResolvesByTeam()
    {
        var handler = CreateHandler(
            MakePlayer("1", "Mike", "Williams", Position.WR, "NYJ"),
            MakePlayer("2", "Mike", "Williams", Position.WR, "LAC"));

        await handler.Handle(new ImportFantasyProsDynastyRankingsCommand(
            Csv("1,\"Mike Williams\",LAC,WR1"), 2026), CancellationToken.None);

        Doc("Mike Williams").SleeperPlayerId.Should().Be("2");
    }

    [Fact]
    public async Task DuplicateNamePositionAndTeam_IsRefused()
    {
        // Nothing left to separate them. A wrong bind writes one player's ranking
        // onto another player's id and reads as real data forever after.
        var handler = CreateHandler(
            MakePlayer("1", "Mike", "Williams", Position.WR, "LAC"),
            MakePlayer("2", "Mike", "Williams", Position.WR, "LAC"));

        await handler.Handle(new ImportFantasyProsDynastyRankingsCommand(
            Csv("1,\"Mike Williams\",LAC,WR1"), 2026), CancellationToken.None);

        Doc("Mike Williams").SleeperPlayerId.Should().BeEmpty();
    }

    // ── Regression guard ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExactMatch_StillWorks_AndUnmatchedRowsAreStillStored()
    {
        var handler = CreateHandler(
            MakePlayer("7000", "Bijan", "Robinson", Position.RB, "ATL"));

        var result = await handler.Handle(new ImportFantasyProsDynastyRankingsCommand(
            Csv("1,\"Bijan Robinson\",ATL,RB1",
                "2,\"Nobody Atall\",FA,WR9"), 2026), CancellationToken.None);

        Doc("Bijan Robinson").SleeperPlayerId.Should().Be("7000");
        Doc("Nobody Atall").SleeperPlayerId.Should().BeEmpty();

        result.IsSuccess.Should().BeTrue();
        result.Value!.Unmatched.Should().Be(1);
        _saved.Should().HaveCount(2);
    }
}

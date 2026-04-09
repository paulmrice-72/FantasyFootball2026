using FF.Application.Features.Team.Queries;
using FF.Application.Interfaces.External;
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Domain.Entities;
using FF.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FF.Tests.Application.Team;

public class GetMyMatchupQueryHandlerTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private const string LeagueId = "league-001";
    private const string SleeperUserId = "user-001";
    private const string MyRosterId = "101";
    private const string OppRosterId = "102";
    private const int MatchupId = 7;
    private const int Season = 2024;
    private const int Week = 1;

    // My rostered player SleeperIds
    private static readonly List<string> MyPlayerIds =
        ["S-QB1", "S-RB1", "S-RB2", "S-WR1", "S-WR2", "S-TE1"];

    // Opponent rostered player SleeperIds
    private static readonly List<string> OppPlayerIds =
        ["O-QB1", "O-RB1", "O-RB2", "O-WR1", "O-WR2", "O-TE1"];

    private static RosterPlayerDocument MakeRosterDoc(
        string sleeperUserId, string sleeperRosterId, List<string> playerIds,
        string teamName = "Team A", string ownerName = "Owner A") => new()
        {
            SleeperUserId = sleeperUserId,
            SleeperRosterId = sleeperRosterId,
            SleeperLeagueId = LeagueId,
            TeamName = teamName,
            OwnerName = ownerName,
            PlayerIds = playerIds,
            StarterIds = playerIds.Take(5).ToList()
        };

    private static Player MakePlayer(string sleeperPlayerId, string pos) =>
        Player.Create(
            firstName: "Player",
            lastName: sleeperPlayerId,
            position: Enum.Parse<Position>(pos),
            nflTeam: "TST",
            sleeperPlayerId: sleeperPlayerId);

    private static SimulationResultDocument MakeSim(
        string sleeperPlayerId,
        decimal median = 14m, decimal floor = 8m, decimal ceiling = 22m) => new()
        {
            SleeperPlayerId = sleeperPlayerId,
            PlayerId = sleeperPlayerId,
            PlayerName = $"Player {sleeperPlayerId}",
            Position = "WR",
            NflTeam = "TST",
            Season = Season,
            Week = Week,
            Median = median,
            Floor = floor,
            Ceiling = ceiling,
            BoomProbability = 0.25m,
            BustProbability = 0.15m,
            ScoringFormat = "HalfPpr",
            CalculatedAt = DateTime.UtcNow
        };

    private static GetMyMatchupQueryHandler BuildHandler(
        ISleeperMatchupService? matchupService = null,
        IRosterPlayerRepository? rosterRepo = null,
        IPlayerRepository? playerRepo = null,
        ISimulationResultRepository? simRepo = null,
        IInjuryAlertRepository? injuryRepo = null)
    {
        matchupService ??= Substitute.For<ISleeperMatchupService>();
        rosterRepo ??= Substitute.For<IRosterPlayerRepository>();
        playerRepo ??= Substitute.For<IPlayerRepository>();
        simRepo ??= Substitute.For<ISimulationResultRepository>();
        injuryRepo ??= Substitute.For<IInjuryAlertRepository>();

        injuryRepo.GetActiveAlertsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        return new GetMyMatchupQueryHandler(
            matchupService, rosterRepo, playerRepo, simRepo, injuryRepo,
            NullLogger<GetMyMatchupQueryHandler>.Instance);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_null_when_sleeper_has_no_matchup_data()
    {
        var matchupSvc = Substitute.For<ISleeperMatchupService>();
        matchupSvc.GetMatchupsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SleeperMatchupEntry>().ToList().AsReadOnly() as IReadOnlyList<SleeperMatchupEntry>);

        var handler = BuildHandler(matchupService: matchupSvc);
        var result = await handler.Handle(
            new GetMyMatchupQuery(SleeperUserId, LeagueId, Season, Week),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Returns_null_when_user_roster_not_found()
    {
        var matchupSvc = Substitute.For<ISleeperMatchupService>();
        matchupSvc.GetMatchupsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<SleeperMatchupEntry>
            {
                new(MatchupId, int.Parse(MyRosterId),  MyPlayerIds,  MyPlayerIds.Take(5).ToList()),
                new(MatchupId, int.Parse(OppRosterId), OppPlayerIds, OppPlayerIds.Take(5).ToList())
            }.AsReadOnly());

        var rosterRepo = Substitute.For<IRosterPlayerRepository>();
        rosterRepo.GetBySleeperUserIdAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((RosterPlayerDocument?)null);

        var handler = BuildHandler(matchupService: matchupSvc, rosterRepo: rosterRepo);
        var result = await handler.Handle(
            new GetMyMatchupQuery(SleeperUserId, LeagueId, Season, Week),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Returns_matchup_with_both_sides_populated()
    {
        // Arrange
        var matchupSvc = Substitute.For<ISleeperMatchupService>();
        matchupSvc.GetMatchupsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<SleeperMatchupEntry>
            {
                new(MatchupId, int.Parse(MyRosterId),  MyPlayerIds,  MyPlayerIds.Take(5).ToList()),
                new(MatchupId, int.Parse(OppRosterId), OppPlayerIds, OppPlayerIds.Take(5).ToList())
            }.AsReadOnly());

        var myRosterDoc = MakeRosterDoc(SleeperUserId, MyRosterId, MyPlayerIds, "Great Jeans", "Paul");
        var oppRosterDoc = MakeRosterDoc("user-002", OppRosterId, OppPlayerIds, "Fire Squad", "John");

        var rosterRepo = Substitute.For<IRosterPlayerRepository>();
        rosterRepo.GetBySleeperUserIdAsync(SleeperUserId, LeagueId, Arg.Any<CancellationToken>())
            .Returns(myRosterDoc);
        rosterRepo.GetByLeagueAsync(LeagueId, Arg.Any<CancellationToken>())
            .Returns(new List<RosterPlayerDocument> { myRosterDoc, oppRosterDoc }.AsReadOnly()
                as IReadOnlyList<RosterPlayerDocument>);

        var allPlayerIds = MyPlayerIds.Concat(OppPlayerIds).ToList();
        var playerRepo = Substitute.For<IPlayerRepository>();
        playerRepo.GetBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(allPlayerIds.Select(id =>
                MakePlayer(id, id.Contains("QB") ? "QB" : id.Contains("RB") ? "RB" :
                               id.Contains("WR") ? "WR" : "TE")).ToList());

        var simRepo = Substitute.For<ISimulationResultRepository>();
        simRepo.GetLatestBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(allPlayerIds.Select(id => MakeSim(id)).ToList().AsReadOnly()
                as IReadOnlyList<SimulationResultDocument>);

        var handler = BuildHandler(matchupSvc, rosterRepo, playerRepo, simRepo);

        // Act
        var result = await handler.Handle(
            new GetMyMatchupQuery(SleeperUserId, LeagueId, Season, Week),
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result!.Week.Should().Be(Week);
        result.Season.Should().Be(Season);
        result.MyTeam.TeamName.Should().Be("Great Jeans");
        result.Opponent.TeamName.Should().Be("Fire Squad");
        result.MyTeam.Players.Should().HaveCount(MyPlayerIds.Count);
        result.Opponent.Players.Should().HaveCount(OppPlayerIds.Count);
    }

    [Fact]
    public async Task Starters_are_separated_from_bench_correctly()
    {
        var myStarters = MyPlayerIds.Take(5).ToList();   // first 5 are starters
        var myBench = MyPlayerIds.Skip(5).ToList();   // last 1 is bench

        var matchupSvc = Substitute.For<ISleeperMatchupService>();
        matchupSvc.GetMatchupsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<SleeperMatchupEntry>
            {
                new(MatchupId, int.Parse(MyRosterId),  myStarters,  MyPlayerIds),
                new(MatchupId, int.Parse(OppRosterId), OppPlayerIds.Take(5).ToList(), OppPlayerIds)
            }.AsReadOnly());

        var myRosterDoc = MakeRosterDoc(SleeperUserId, MyRosterId, MyPlayerIds, "Great Jeans", "Paul");
        var oppRosterDoc = MakeRosterDoc("user-002", OppRosterId, OppPlayerIds, "Fire Squad", "John");

        var rosterRepo = Substitute.For<IRosterPlayerRepository>();
        rosterRepo.GetBySleeperUserIdAsync(SleeperUserId, LeagueId, Arg.Any<CancellationToken>())
            .Returns(myRosterDoc);
        rosterRepo.GetByLeagueAsync(LeagueId, Arg.Any<CancellationToken>())
            .Returns(new List<RosterPlayerDocument> { myRosterDoc, oppRosterDoc }.AsReadOnly()
                as IReadOnlyList<RosterPlayerDocument>);

        var allIds = MyPlayerIds.Concat(OppPlayerIds).ToList();
        var playerRepo = Substitute.For<IPlayerRepository>();
        playerRepo.GetBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(allIds.Select(id => MakePlayer(id, "WR")).ToList());

        var simRepo = Substitute.For<ISimulationResultRepository>();
        simRepo.GetLatestBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(allIds.Select(id => MakeSim(id)).ToList().AsReadOnly()
                as IReadOnlyList<SimulationResultDocument>);

        var handler = BuildHandler(matchupSvc, rosterRepo, playerRepo, simRepo);
        var result = await handler.Handle(
            new GetMyMatchupQuery(SleeperUserId, LeagueId, Season, Week),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.MyTeam.Players.Count(p => p.IsStarter).Should().Be(5);
        result.MyTeam.Players.Count(p => !p.IsStarter).Should().Be(1);

        // Projected total should only sum starters
        result.MyTeam.TotalProjectedPoints.Should().BeApproximately(5 * 14.0, 0.1);
    }

    [Fact]
    public async Task Win_probability_sums_to_one()
    {
        var matchupSvc = Substitute.For<ISleeperMatchupService>();
        matchupSvc.GetMatchupsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<SleeperMatchupEntry>
            {
                new(MatchupId, int.Parse(MyRosterId),  MyPlayerIds,  MyPlayerIds.Take(5).ToList()),
                new(MatchupId, int.Parse(OppRosterId), OppPlayerIds, OppPlayerIds.Take(5).ToList())
            }.AsReadOnly());

        var myRosterDoc = MakeRosterDoc(SleeperUserId, MyRosterId, MyPlayerIds, "Team A", "Paul");
        var oppRosterDoc = MakeRosterDoc("user-002", OppRosterId, OppPlayerIds, "Team B", "John");

        var rosterRepo = Substitute.For<IRosterPlayerRepository>();
        rosterRepo.GetBySleeperUserIdAsync(SleeperUserId, LeagueId, Arg.Any<CancellationToken>())
            .Returns(myRosterDoc);
        rosterRepo.GetByLeagueAsync(LeagueId, Arg.Any<CancellationToken>())
            .Returns(new List<RosterPlayerDocument> { myRosterDoc, oppRosterDoc }.AsReadOnly()
                as IReadOnlyList<RosterPlayerDocument>);

        var allIds = MyPlayerIds.Concat(OppPlayerIds).ToList();
        var playerRepo = Substitute.For<IPlayerRepository>();
        playerRepo.GetBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(allIds.Select(id => MakePlayer(id, "WR")).ToList());

        var simRepo = Substitute.For<ISimulationResultRepository>();
        simRepo.GetLatestBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(allIds.Select(id => MakeSim(id)).ToList().AsReadOnly()
                as IReadOnlyList<SimulationResultDocument>);

        var handler = BuildHandler(matchupSvc, rosterRepo, playerRepo, simRepo);
        var result = await handler.Handle(
            new GetMyMatchupQuery(SleeperUserId, LeagueId, Season, Week),
            CancellationToken.None);

        result.Should().NotBeNull();
        (result!.MyWinProbability + result.OpponentWinProbability)
            .Should().BeApproximately(1.0, 0.001);
    }

    [Fact]
    public async Task Higher_projected_team_has_higher_win_probability()
    {
        // My team gets 20pt median players, opponent gets 10pt median players
        var matchupSvc = Substitute.For<ISleeperMatchupService>();
        matchupSvc.GetMatchupsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<SleeperMatchupEntry>
            {
                new(MatchupId, int.Parse(MyRosterId),  MyPlayerIds,  MyPlayerIds.Take(5).ToList()),
                new(MatchupId, int.Parse(OppRosterId), OppPlayerIds, OppPlayerIds.Take(5).ToList())
            }.AsReadOnly());

        var myRosterDoc = MakeRosterDoc(SleeperUserId, MyRosterId, MyPlayerIds, "Strong Team", "Paul");
        var oppRosterDoc = MakeRosterDoc("user-002", OppRosterId, OppPlayerIds, "Weak Team", "John");

        var rosterRepo = Substitute.For<IRosterPlayerRepository>();
        rosterRepo.GetBySleeperUserIdAsync(SleeperUserId, LeagueId, Arg.Any<CancellationToken>())
            .Returns(myRosterDoc);
        rosterRepo.GetByLeagueAsync(LeagueId, Arg.Any<CancellationToken>())
            .Returns(new List<RosterPlayerDocument> { myRosterDoc, oppRosterDoc }.AsReadOnly()
                as IReadOnlyList<RosterPlayerDocument>);

        var playerRepo = Substitute.For<IPlayerRepository>();
        playerRepo.GetBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(MyPlayerIds.Concat(OppPlayerIds)
                .Select(id => MakePlayer(id, "WR")).ToList());

        // My players project 20pts median, opponent projects 10pts
        var simRepo = Substitute.For<ISimulationResultRepository>();
        simRepo.GetLatestBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(
                MyPlayerIds.Select(id => MakeSim(id, median: 20m, floor: 14m, ceiling: 30m))
                .Concat(OppPlayerIds.Select(id => MakeSim(id, median: 10m, floor: 6m, ceiling: 16m)))
                .ToList().AsReadOnly() as IReadOnlyList<SimulationResultDocument>);

        var handler = BuildHandler(matchupSvc, rosterRepo, playerRepo, simRepo);
        var result = await handler.Handle(
            new GetMyMatchupQuery(SleeperUserId, LeagueId, Season, Week),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.MyWinProbability.Should().BeGreaterThan(0.5);
        result.OpponentWinProbability.Should().BeLessThan(0.5);
    }
}
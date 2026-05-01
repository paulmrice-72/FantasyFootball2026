using FF.Application.Features.Team.Queries;
using FF.Application.Interfaces.External;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
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
    // ── Fixtures ────────────────────────────────────────────────────────────────
    private const string LeagueId = "league-001";
    private const string SleeperUserId = "user-001";
    private const string MyRosterId = "101";
    private const string OppRosterId = "102";
    private const int MatchupId = 7;
    private const int Season = 2024;
    private const int Week = 1;

    private static readonly List<string> MyPlayerIds = ["S-QB1", "S-RB1", "S-RB2", "S-WR1", "S-WR2", "S-TE1"];
    private static readonly List<string> OppPlayerIds = ["O-QB1", "O-RB1", "O-RB2", "O-WR1", "O-WR2", "O-TE1"];

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
        decimal median = 14m,
        decimal floor = 8m,
        decimal ceiling = 22m) => new()
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

    private static PlayerProjectionDocument MakeProjection(string sleeperPlayerId) => new()
    {
        SleeperPlayerId = sleeperPlayerId,
        PlayerId = sleeperPlayerId,
        PlayerName = $"Player {sleeperPlayerId}",
        Position = "WR",
        NflTeam = "TST",
        Season = Season,
        Week = Week,
        ProjectedPoints = 12m,
        ProjectedPointsPpr = 14m,
        ProjectedPointsHalfPpr = 13m,
        WeightedAvgPoints = 12.5m,
        MatchupAdjustmentFactor = 1.05m,
        SnapPctInput = 0.72m,
        TargetShareInput = 0.18m,
        GameScript = "Favorable",
        SpreadInput = -3.5m,
        ScoringFormat = "HalfPpr",
        GameSampleSize = 8,
        RSquared = 0.78m,
        CalculatedAt = DateTime.UtcNow
    };

    private static GetMyMatchupQueryHandler BuildHandler(
        ISleeperMatchupService? matchupService = null,
        IRosterPlayerRepository? rosterRepo = null,
        IPlayerRepository? playerRepo = null,
        ISimulationResultRepository? simRepo = null,
        IInjuryAlertRepository? injuryRepo = null,
        ILeagueRepository? leagueRepo = null,
        ILeagueContextResolverService? leagueCtxResolver = null,
        IPlayerProjectionRepository? projectionRepo = null)
    {
        matchupService ??= Substitute.For<ISleeperMatchupService>();
        rosterRepo ??= Substitute.For<IRosterPlayerRepository>();
        playerRepo ??= Substitute.For<IPlayerRepository>();
        simRepo ??= Substitute.For<ISimulationResultRepository>();
        injuryRepo ??= Substitute.For<IInjuryAlertRepository>();
        leagueRepo ??= Substitute.For<ILeagueRepository>();
        leagueCtxResolver ??= Substitute.For<ILeagueContextResolverService>();

        // Only set up default empty return if caller didn't supply a pre-configured substitute
        var projRepoWasSupplied = projectionRepo is not null;
        projectionRepo ??= Substitute.For<IPlayerProjectionRepository>();

        injuryRepo.GetActiveAlertsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns([]);

        leagueRepo.GetBySleeperIdAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((FF.Domain.Entities.League?)null);

        if (!projRepoWasSupplied)
        {
            projectionRepo.GetBySleeperIdsAsync(
                    Arg.Any<IEnumerable<string>>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns([]);
        }

        return new GetMyMatchupQueryHandler(
            matchupService,
            rosterRepo,
            playerRepo,
            simRepo,
            injuryRepo,
            leagueRepo,
            leagueCtxResolver,
            projectionRepo,
            NullLogger<GetMyMatchupQueryHandler>.Instance);
    }

    // ── Tests ───────────────────────────────────────────────────────────────────

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
                new(MatchupId, int.Parse(MyRosterId), MyPlayerIds, MyPlayerIds.Take(5).ToList()),
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
        var matchupSvc = Substitute.For<ISleeperMatchupService>();
        matchupSvc.GetMatchupsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<SleeperMatchupEntry>
            {
                new(MatchupId, int.Parse(MyRosterId), MyPlayerIds, MyPlayerIds.Take(5).ToList()),
                new(MatchupId, int.Parse(OppRosterId), OppPlayerIds, OppPlayerIds.Take(5).ToList())
            }.AsReadOnly());

        var myRosterDoc = MakeRosterDoc(SleeperUserId, MyRosterId, MyPlayerIds, "Great Jeans", "Paul");
        var oppRosterDoc = MakeRosterDoc("user-002", OppRosterId, OppPlayerIds, "Fire Squad", "John");

        var rosterRepo = Substitute.For<IRosterPlayerRepository>();
        rosterRepo.GetBySleeperUserIdAsync(SleeperUserId, LeagueId, Arg.Any<CancellationToken>())
            .Returns(myRosterDoc);
        rosterRepo.GetByLeagueAsync(LeagueId, Arg.Any<CancellationToken>())
            .Returns(new List<RosterPlayerDocument> { myRosterDoc, oppRosterDoc }
                .AsReadOnly() as IReadOnlyList<RosterPlayerDocument>);

        var allPlayerIds = MyPlayerIds.Concat(OppPlayerIds).ToList();

        var playerRepo = Substitute.For<IPlayerRepository>();
        playerRepo.GetBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(allPlayerIds.Select(id =>
                MakePlayer(id, id.Contains("QB") ? "QB" : id.Contains("RB") ? "RB" : id.Contains("WR") ? "WR" : "TE"))
                .ToList());

        var simRepo = Substitute.For<ISimulationResultRepository>();
        simRepo.GetLatestBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(allPlayerIds.Select(id => MakeSim(id)).ToList()
                .AsReadOnly() as IReadOnlyList<SimulationResultDocument>);

        var handler = BuildHandler(matchupSvc, rosterRepo, playerRepo, simRepo);
        var result = await handler.Handle(
            new GetMyMatchupQuery(SleeperUserId, LeagueId, Season, Week),
            CancellationToken.None);

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
        var myStarters = MyPlayerIds.Take(5).ToList();

        var matchupSvc = Substitute.For<ISleeperMatchupService>();
        matchupSvc.GetMatchupsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<SleeperMatchupEntry>
            {
                new(MatchupId, int.Parse(MyRosterId), myStarters, MyPlayerIds),
                new(MatchupId, int.Parse(OppRosterId), OppPlayerIds.Take(5).ToList(), OppPlayerIds)
            }.AsReadOnly());

        var myRosterDoc = MakeRosterDoc(SleeperUserId, MyRosterId, MyPlayerIds, "Great Jeans", "Paul");
        var oppRosterDoc = MakeRosterDoc("user-002", OppRosterId, OppPlayerIds, "Fire Squad", "John");

        var rosterRepo = Substitute.For<IRosterPlayerRepository>();
        rosterRepo.GetBySleeperUserIdAsync(SleeperUserId, LeagueId, Arg.Any<CancellationToken>())
            .Returns(myRosterDoc);
        rosterRepo.GetByLeagueAsync(LeagueId, Arg.Any<CancellationToken>())
            .Returns(new List<RosterPlayerDocument> { myRosterDoc, oppRosterDoc }
                .AsReadOnly() as IReadOnlyList<RosterPlayerDocument>);

        var allIds = MyPlayerIds.Concat(OppPlayerIds).ToList();

        var playerRepo = Substitute.For<IPlayerRepository>();
        playerRepo.GetBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(allIds.Select(id => MakePlayer(id, "WR")).ToList());

        var simRepo = Substitute.For<ISimulationResultRepository>();
        simRepo.GetLatestBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(allIds.Select(id => MakeSim(id)).ToList()
                .AsReadOnly() as IReadOnlyList<SimulationResultDocument>);

        var handler = BuildHandler(matchupSvc, rosterRepo, playerRepo, simRepo);
        var result = await handler.Handle(
            new GetMyMatchupQuery(SleeperUserId, LeagueId, Season, Week),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.MyTeam.Players.Count(p => p.IsStarter).Should().Be(5);
        result.MyTeam.Players.Count(p => !p.IsStarter).Should().Be(1);
        result.MyTeam.TotalProjectedPoints.Should().BeApproximately(5 * 14.0, 0.1);
    }

    [Fact]
    public async Task Win_probability_sums_to_one()
    {
        var matchupSvc = Substitute.For<ISleeperMatchupService>();
        matchupSvc.GetMatchupsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<SleeperMatchupEntry>
            {
                new(MatchupId, int.Parse(MyRosterId), MyPlayerIds, MyPlayerIds.Take(5).ToList()),
                new(MatchupId, int.Parse(OppRosterId), OppPlayerIds, OppPlayerIds.Take(5).ToList())
            }.AsReadOnly());

        var myRosterDoc = MakeRosterDoc(SleeperUserId, MyRosterId, MyPlayerIds, "Team A", "Paul");
        var oppRosterDoc = MakeRosterDoc("user-002", OppRosterId, OppPlayerIds, "Team B", "John");

        var rosterRepo = Substitute.For<IRosterPlayerRepository>();
        rosterRepo.GetBySleeperUserIdAsync(SleeperUserId, LeagueId, Arg.Any<CancellationToken>())
            .Returns(myRosterDoc);
        rosterRepo.GetByLeagueAsync(LeagueId, Arg.Any<CancellationToken>())
            .Returns(new List<RosterPlayerDocument> { myRosterDoc, oppRosterDoc }
                .AsReadOnly() as IReadOnlyList<RosterPlayerDocument>);

        var allIds = MyPlayerIds.Concat(OppPlayerIds).ToList();

        var playerRepo = Substitute.For<IPlayerRepository>();
        playerRepo.GetBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(allIds.Select(id => MakePlayer(id, "WR")).ToList());

        var simRepo = Substitute.For<ISimulationResultRepository>();
        simRepo.GetLatestBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(allIds.Select(id => MakeSim(id)).ToList()
                .AsReadOnly() as IReadOnlyList<SimulationResultDocument>);

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
        var matchupSvc = Substitute.For<ISleeperMatchupService>();
        matchupSvc.GetMatchupsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<SleeperMatchupEntry>
            {
                new(MatchupId, int.Parse(MyRosterId), MyPlayerIds, MyPlayerIds.Take(5).ToList()),
                new(MatchupId, int.Parse(OppRosterId), OppPlayerIds, OppPlayerIds.Take(5).ToList())
            }.AsReadOnly());

        var myRosterDoc = MakeRosterDoc(SleeperUserId, MyRosterId, MyPlayerIds, "Strong Team", "Paul");
        var oppRosterDoc = MakeRosterDoc("user-002", OppRosterId, OppPlayerIds, "Weak Team", "John");

        var rosterRepo = Substitute.For<IRosterPlayerRepository>();
        rosterRepo.GetBySleeperUserIdAsync(SleeperUserId, LeagueId, Arg.Any<CancellationToken>())
            .Returns(myRosterDoc);
        rosterRepo.GetByLeagueAsync(LeagueId, Arg.Any<CancellationToken>())
            .Returns(new List<RosterPlayerDocument> { myRosterDoc, oppRosterDoc }
                .AsReadOnly() as IReadOnlyList<RosterPlayerDocument>);

        var playerRepo = Substitute.For<IPlayerRepository>();
        playerRepo.GetBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(MyPlayerIds.Concat(OppPlayerIds).Select(id => MakePlayer(id, "WR")).ToList());

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

    [Fact]
    public async Task Sleeper_empty_slot_placeholders_are_not_marked_as_starters()
    {
        var startersWithPlaceholder = new List<string> { "S-QB1", "0", "S-WR1", "0", "S-RB1" };

        var matchupSvc = Substitute.For<ISleeperMatchupService>();
        matchupSvc.GetMatchupsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<SleeperMatchupEntry>
            {
                new(MatchupId, int.Parse(MyRosterId), startersWithPlaceholder, MyPlayerIds),
                new(MatchupId, int.Parse(OppRosterId), OppPlayerIds.Take(5).ToList(), OppPlayerIds)
            }.AsReadOnly());

        var myRosterDoc = MakeRosterDoc(SleeperUserId, MyRosterId, MyPlayerIds, "Team A", "Paul");
        var oppRosterDoc = MakeRosterDoc("user-002", OppRosterId, OppPlayerIds, "Team B", "John");

        var rosterRepo = Substitute.For<IRosterPlayerRepository>();
        rosterRepo.GetBySleeperUserIdAsync(SleeperUserId, LeagueId, Arg.Any<CancellationToken>())
            .Returns(myRosterDoc);
        rosterRepo.GetByLeagueAsync(LeagueId, Arg.Any<CancellationToken>())
            .Returns(new List<RosterPlayerDocument> { myRosterDoc, oppRosterDoc }
                .AsReadOnly() as IReadOnlyList<RosterPlayerDocument>);

        var allIds = MyPlayerIds.Concat(OppPlayerIds).ToList();

        var playerRepo = Substitute.For<IPlayerRepository>();
        playerRepo.GetBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(allIds.Select(id => MakePlayer(id, "WR")).ToList());

        var simRepo = Substitute.For<ISimulationResultRepository>();
        simRepo.GetLatestBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(allIds.Select(id => MakeSim(id)).ToList()
                .AsReadOnly() as IReadOnlyList<SimulationResultDocument>);

        var handler = BuildHandler(matchupSvc, rosterRepo, playerRepo, simRepo);
        var result = await handler.Handle(
            new GetMyMatchupQuery(SleeperUserId, LeagueId, Season, Week),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.MyTeam.Players.Where(p => p.IsStarter)
            .Should().NotContain(p => p.SleeperPlayerId == "0");
        result.MyTeam.Players.Count(p => p.IsStarter).Should().Be(3);
    }

    [Fact]
    public async Task Projection_breakdown_is_populated_when_projection_data_exists()
    {
        // Arrange
        var matchupSvc = Substitute.For<ISleeperMatchupService>();
        matchupSvc.GetMatchupsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<SleeperMatchupEntry>
            {
                new(MatchupId, int.Parse(MyRosterId), MyPlayerIds.Take(5).ToList(), MyPlayerIds),
                new(MatchupId, int.Parse(OppRosterId), OppPlayerIds.Take(5).ToList(), OppPlayerIds)
            }.AsReadOnly());

        var myRosterDoc = MakeRosterDoc(SleeperUserId, MyRosterId, MyPlayerIds, "Great Jeans", "Paul");
        var oppRosterDoc = MakeRosterDoc("user-002", OppRosterId, OppPlayerIds, "Fire Squad", "John");

        var rosterRepo = Substitute.For<IRosterPlayerRepository>();
        rosterRepo.GetBySleeperUserIdAsync(SleeperUserId, LeagueId, Arg.Any<CancellationToken>())
            .Returns(myRosterDoc);
        rosterRepo.GetByLeagueAsync(LeagueId, Arg.Any<CancellationToken>())
            .Returns(new List<RosterPlayerDocument> { myRosterDoc, oppRosterDoc }
                .AsReadOnly() as IReadOnlyList<RosterPlayerDocument>);

        var allIds = MyPlayerIds.Concat(OppPlayerIds).ToList();

        var playerRepo = Substitute.For<IPlayerRepository>();
        playerRepo.GetBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(allIds.Select(id => MakePlayer(id, "WR")).ToList());

        var simRepo = Substitute.For<ISimulationResultRepository>();
        simRepo.GetLatestBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(allIds.Select(id => MakeSim(id)).ToList()
                .AsReadOnly() as IReadOnlyList<SimulationResultDocument>);

        // Set up projections for my players only
        var projRepo = Substitute.For<IPlayerProjectionRepository>();
        projRepo.GetBySleeperIdsAsync(
                Arg.Any<IEnumerable<string>>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(MyPlayerIds.Select(id => MakeProjection(id)).ToList());

        var handler = BuildHandler(matchupSvc, rosterRepo, playerRepo, simRepo, projectionRepo: projRepo);

        // Act
        var result = await handler.Handle(
            new GetMyMatchupQuery(SleeperUserId, LeagueId, Season, Week),
            CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        // My players should have a projection breakdown
        result!.MyTeam.Players
            .Where(p => MyPlayerIds.Contains(p.SleeperPlayerId))
            .Should().AllSatisfy(p => p.ProjectionBreakdown.Should().NotBeNull());

        // Opponent players (no projections returned for them) should have null breakdown
        result.Opponent.Players
            .Should().AllSatisfy(p => p.ProjectionBreakdown.Should().BeNull());

        // Spot check breakdown values
        var qb = result.MyTeam.Players.First(p => p.SleeperPlayerId == "S-QB1");
        qb.ProjectionBreakdown!.MatchupAdjustmentFactor.Should().BeApproximately(1.05, 0.001);
        qb.ProjectionBreakdown.GameScript.Should().Be("Favorable");
        qb.ProjectionBreakdown.SnapPctInput.Should().BeApproximately(0.72, 0.001);
    }
}
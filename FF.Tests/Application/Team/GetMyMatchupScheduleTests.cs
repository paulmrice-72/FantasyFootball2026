using FF.Application.Features.Team.Queries;
using FF.Application.Interfaces.External;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Domain.Entities;
using FF.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FF.Tests.Application.Team;

/// <summary>
/// FAN-178. The matchup label and the side total, both resolved from
/// <c>nfl_schedule</c>.
///
/// <para>
/// These cover the seam the original defect lived in rather than the code that
/// was changed. The opponent used to be read off the simulation document, which
/// carried whatever the last projection run stamped — in a carryover run, the
/// player's final opponent of the PRIOR season. Reading the schedule at query
/// time means the label is right as soon as the schedule is imported, and these
/// tests pin that it is keyed on the player's CURRENT team rather than anything
/// a stale document remembers.
/// </para>
/// </summary>
public class GetMyMatchupScheduleTests
{
    private const string LeagueId = "league-001";
    private const string SleeperUserId = "user-001";
    private const string MyRosterId = "101";
    private const string OppRosterId = "102";
    private const int MatchupId = 7;
    private const int Season = 2026;
    private const int Week = 1;

    // Walker is the live case: a Seahawk in the 2025 logs, a Chief in 2026.
    private const string WalkerId = "S-RB-WALKER";
    private const string OlaveId = "S-WR-OLAVE";
    private const string StevensonId = "S-RB-STEVENSON";
    private const string ByePlayerId = "S-WR-BYE";
    private const string RamsId = "S-WR-RAM";

    // Every id here must be on the roster: the handler builds its rows from the
    // matchup entry's Players list, so a player present only in the lookup
    // substitutes is simply never constructed.
    private static readonly List<string> MyPlayerIds =
        [WalkerId, OlaveId, StevensonId, ByePlayerId, RamsId];
    private static readonly List<string> OppPlayerIds = ["O-WR1", "O-WR2", "O-WR3", "O-WR4"];

    private static RosterPlayerDocument MakeRosterDoc(
        string userId, string rosterId, List<string> playerIds, string teamName) => new()
        {
            SleeperUserId = userId,
            SleeperRosterId = rosterId,
            SleeperLeagueId = LeagueId,
            TeamName = teamName,
            OwnerName = "Owner",
            PlayerIds = playerIds,
            StarterIds = playerIds
        };

    private static Player MakePlayer(string id, string nflTeam, string pos = "WR") =>
        Player.Create("Player", id, Enum.Parse<Position>(pos), nflTeam, id);

    private static SimulationResultDocument MakeSim(
        string id, decimal mean = 14m, decimal floor = 8m, decimal ceiling = 22m,
        string? staleOpponent = null) => new()
        {
            SleeperPlayerId = id,
            PlayerId = id,
            PlayerName = $"Player {id}",
            Position = "WR",
            NflTeam = "TST",
            Season = Season,
            Week = Week,
            Median = mean,
            Mean = mean,
            Floor = floor,
            Ceiling = ceiling,
            // Deliberately wrong where set — the handler must ignore it.
            OpponentTeam = staleOpponent ?? string.Empty,
            ScoringFormat = "FullPpr",
            CalculatedAt = DateTime.UtcNow
        };

    private static NflScheduleDocument Game(
        string away, string home, bool final = false, int week = Week) => new()
        {
            GameId = $"{Season}_{week:00}_{away}_{home}",
            Season = Season,
            Week = week,
            GameType = "REG",
            Gameday = new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc),
            GametimeEt = "13:00",
            Weekday = "Sunday",
            AwayTeam = away,
            HomeTeam = home,
            AwayScore = final ? 10 : null,
            HomeScore = final ? 13 : null,
            Location = "Home"
        };

    private static GetMyMatchupQueryHandler BuildHandler(
        IReadOnlyList<NflScheduleDocument> schedule,
        IReadOnlyList<Player> players,
        IReadOnlyList<SimulationResultDocument> sims,
        Dictionary<string, decimal>? myPlayersPoints = null)
    {
        var matchupSvc = Substitute.For<ISleeperMatchupService>();
        matchupSvc.GetMatchupsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<SleeperMatchupEntry>
            {
                new(MatchupId, int.Parse(MyRosterId), MyPlayerIds, MyPlayerIds, null, myPlayersPoints),
                new(MatchupId, int.Parse(OppRosterId), OppPlayerIds, OppPlayerIds, null, null)
            }.AsReadOnly());

        var myRoster = MakeRosterDoc(SleeperUserId, MyRosterId, MyPlayerIds, "Amite Aardvarks");
        var oppRoster = MakeRosterDoc("user-002", OppRosterId, OppPlayerIds, "WIC Office");

        var rosterRepo = Substitute.For<IRosterPlayerRepository>();
        rosterRepo.GetBySleeperUserIdAsync(SleeperUserId, LeagueId, Arg.Any<CancellationToken>())
            .Returns(myRoster);
        rosterRepo.GetByLeagueAsync(LeagueId, Arg.Any<CancellationToken>())
            .Returns(new List<RosterPlayerDocument> { myRoster, oppRoster }
                .AsReadOnly() as IReadOnlyList<RosterPlayerDocument>);

        var playerRepo = Substitute.For<IPlayerRepository>();
        playerRepo.GetBySleeperIdsAsync(Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(players);

        var simRepo = Substitute.For<ISimulationResultRepository>();
        simRepo.GetLatestBySleeperIdsAsync(
                Arg.Any<IEnumerable<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(sims);

        var injuryRepo = Substitute.For<IInjuryAlertRepository>();
        injuryRepo.GetActiveAlertsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns([]);

        var leagueRepo = Substitute.For<ILeagueRepository>();
        leagueRepo.GetBySleeperIdAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((League?)null);

        var projectionRepo = Substitute.For<IPlayerProjectionRepository>();
        projectionRepo.GetBySleeperIdsAsync(
                Arg.Any<IEnumerable<string>>(), Arg.Any<int>(), Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns([]);

        var scheduleRepo = Substitute.For<INflScheduleRepository>();
        scheduleRepo.GetByWeekAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(schedule);

        return new GetMyMatchupQueryHandler(
            matchupSvc, rosterRepo, playerRepo, simRepo, injuryRepo, leagueRepo,
            Substitute.For<ILeagueContextResolverService>(), projectionRepo, scheduleRepo,
            NullLogger<GetMyMatchupQueryHandler>.Instance);
    }

    private static async Task<MyMatchupPlayerDto> PlayerFrom(
        GetMyMatchupQueryHandler handler, string sleeperId)
    {
        var result = await handler.Handle(
            new GetMyMatchupQuery(SleeperUserId, LeagueId, Season, Week), CancellationToken.None);

        result.Should().NotBeNull();
        return result!.MyTeam.Players.Single(p => p.SleeperPlayerId == sleeperId);
    }

    // ── Opponent resolution ───────────────────────────────────────────────

    /// <summary>
    /// The original defect, inverted into a test: the simulation document carries
    /// a stale opponent and the handler must ignore it entirely.
    /// </summary>
    [Fact]
    public async Task Opponent_ComesFromTheSchedule_NotFromAStaleSimulationDocument()
    {
        var handler = BuildHandler(
            schedule: [Game(away: "DEN", home: "KC")],
            players: [MakePlayer(WalkerId, "KC", "RB")],
            sims: [MakeSim(WalkerId, staleOpponent: "SF")]);   // last season's opponent

        var walker = await PlayerFrom(handler, WalkerId);

        walker.OpponentTeam.Should().Be("DEN", "the schedule says DEN @ KC; the sim says SF");
        walker.IsHomeGame.Should().BeTrue();
    }

    [Fact]
    public async Task AwayGame_IsMarkedAsSuch()
    {
        var handler = BuildHandler(
            schedule: [Game(away: "NO", home: "DET")],
            players: [MakePlayer(OlaveId, "NO")],
            sims: [MakeSim(OlaveId)]);

        var olave = await PlayerFrom(handler, OlaveId);

        olave.OpponentTeam.Should().Be("DET");
        olave.IsHomeGame.Should().BeFalse("NO @ DET renders as '@ DET', not 'vs DET'");
    }

    /// <summary>
    /// The join that would silently drop the Rams if either side skipped
    /// normalisation: Sleeper writes LAR on the roster, nflverse writes LA in the
    /// schedule feed.
    /// </summary>
    [Fact]
    public async Task RamsPlayer_MatchesAScheduleRowWrittenAsLA()
    {
        var lar = Game(away: "SF", home: "LAR");   // stored normalised by the importer

        var handler = BuildHandler(
            schedule: [lar],
            players: [MakePlayer(RamsId, "LAR")],
            sims: [MakeSim(RamsId)]);

        var result = await handler.Handle(
            new GetMyMatchupQuery(SleeperUserId, LeagueId, Season, Week), CancellationToken.None);

        result!.MyTeam.Players
            .Single(p => p.SleeperPlayerId == RamsId)
            .OpponentTeam.Should().Be("SF");
    }

    [Fact]
    public async Task ByeWeek_HasNoOpponent_AndIsDistinctFromAnAwayGame()
    {
        var handler = BuildHandler(
            schedule: [Game(away: "DEN", home: "KC")],       // the bye player's team is absent
            players: [MakePlayer(ByePlayerId, "MIA")],
            sims: [MakeSim(ByePlayerId)]);

        var onBye = await PlayerFrom(handler, ByePlayerId);

        onBye.OpponentTeam.Should().BeNull();
        onBye.IsHomeGame.Should().BeNull(
            "null means no game at all — false would render as an away game");
        onBye.IsGameFinal.Should().BeFalse();
    }

    [Fact]
    public async Task NoScheduleImported_LeavesLabelsBlank_RatherThanGuessing()
    {
        var handler = BuildHandler(
            schedule: [],
            players: [MakePlayer(OlaveId, "NO")],
            sims: [MakeSim(OlaveId)]);

        var olave = await PlayerFrom(handler, OlaveId);

        olave.OpponentTeam.Should().BeNull();
    }

    // ── Side total ────────────────────────────────────────────────────────

    /// <summary>
    /// The 16-point header discrepancy: banked actuals were displayed beside the
    /// total but excluded from it, so the number disagreed with its own rows.
    /// </summary>
    [Fact]
    public async Task SideTotal_UsesActualPoints_OnceAGameIsFinal()
    {
        var handler = BuildHandler(
            schedule:
            [
                Game(away: "NE", home: "SEA", final: true),   // Stevenson's game, played
                Game(away: "DEN", home: "KC")                 // Walker's, not yet
            ],
            players:
            [
                MakePlayer(StevensonId, "NE", "RB"),
                MakePlayer(WalkerId, "KC", "RB")
            ],
            sims:
            [
                MakeSim(StevensonId, mean: 12.4m),
                MakeSim(WalkerId, mean: 12.8m)
            ],
            myPlayersPoints: new Dictionary<string, decimal> { [StevensonId] = 15.25m });

        var result = await handler.Handle(
            new GetMyMatchupQuery(SleeperUserId, LeagueId, Season, Week), CancellationToken.None);

        result!.MyTeam.TotalProjectedPoints.Should().BeApproximately(
            15.25 + 12.8, 0.01,
            "Stevenson's game is final so his banked 15.25 replaces the 12.4 projection, " +
            "while Walker's game has not started and keeps his");
    }

    [Fact]
    public async Task SideTotal_KeepsTheProjection_WhileAGameIsStillToBePlayed()
    {
        var handler = BuildHandler(
            schedule: [Game(away: "DEN", home: "KC")],
            players: [MakePlayer(WalkerId, "KC", "RB")],
            sims: [MakeSim(WalkerId, mean: 12.8m)],
            // A live partial score must NOT be treated as final.
            myPlayersPoints: new Dictionary<string, decimal> { [WalkerId] = 3.1m });

        var result = await handler.Handle(
            new GetMyMatchupQuery(SleeperUserId, LeagueId, Season, Week), CancellationToken.None);

        result!.MyTeam.TotalProjectedPoints.Should().BeApproximately(12.8, 0.01,
            "an in-progress game keeps its full projection — blending live actuals with " +
            "remaining expectation is a separate piece of work");
    }

    /// <summary>
    /// A decided game carries no remaining uncertainty. Leaving it in kept the
    /// floor-ceiling band at full width for results that could no longer move.
    /// </summary>
    [Fact]
    public async Task FinishedGames_DoNotWidenTheFloorCeilingBand()
    {
        var handler = BuildHandler(
            schedule: [Game(away: "NE", home: "SEA", final: true)],
            players: [MakePlayer(StevensonId, "NE", "RB")],
            sims: [MakeSim(StevensonId, mean: 12.4m, floor: 6.5m, ceiling: 19.6m)],
            myPlayersPoints: new Dictionary<string, decimal> { [StevensonId] = 15.25m });

        var result = await handler.Handle(
            new GetMyMatchupQuery(SleeperUserId, LeagueId, Season, Week), CancellationToken.None);

        var side = result!.MyTeam;
        side.ProjectedFloor.Should().BeApproximately(side.TotalProjectedPoints, 0.01);
        side.ProjectedCeiling.Should().BeApproximately(side.TotalProjectedPoints, 0.01);
    }

    [Fact]
    public async Task GameFinalFlag_IsSet_FromTheSchedulesScores()
    {
        var handler = BuildHandler(
            schedule: [Game(away: "NE", home: "SEA", final: true)],
            players: [MakePlayer(StevensonId, "NE", "RB")],
            sims: [MakeSim(StevensonId)]);

        var stevenson = await PlayerFrom(handler, StevensonId);

        stevenson.IsGameFinal.Should().BeTrue();
        stevenson.IsHomeGame.Should().BeFalse("NE @ SEA");
        stevenson.OpponentTeam.Should().Be("SEA");
    }
}

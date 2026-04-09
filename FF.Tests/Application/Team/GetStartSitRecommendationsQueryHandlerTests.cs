// FF.Tests/Application/Team/GetStartSitRecommendationsQueryHandlerTests.cs
using FF.Application.Features.Team.Queries;
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Domain.Entities;
using FF.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FF.Tests.Application.Team;

public class GetStartSitRecommendationsQueryHandlerTests
{
    // ── Fixtures ──────────────────────────────────────────────────────────────

    private const string LeagueId = "league-001";
    private const string SleeperUserId = "user-001";
    private const int Season = 2024;
    private const int Week = 1;

    private static SimulationResultDocument MakeSim(
        string sleeperPlayerId, string pos,
        decimal median, decimal floor, decimal ceiling,
        decimal boom = 0.25m, decimal bust = 0.15m) => new()
        {
            SleeperPlayerId = sleeperPlayerId,
            PlayerId = sleeperPlayerId,
            PlayerName = $"Player {sleeperPlayerId}",
            Position = pos,
            NflTeam = "TST",
            Season = Season,
            Week = Week,
            Median = median,
            Floor = floor,
            Ceiling = ceiling,
            BoomProbability = boom,
            BustProbability = bust,
            ScoringFormat = "HalfPpr",
            CalculatedAt = DateTime.UtcNow
        };

    private static Player MakePlayer(string sleeperPlayerId, string pos) =>
        Player.Create(
            firstName: "Player",
            lastName: sleeperPlayerId,
            position: Enum.Parse<Position>(pos),
            nflTeam: "TST",
            sleeperPlayerId: sleeperPlayerId);

    private static RosterPlayerDocument MakeRosterDoc(
        List<string> playerIds,
        List<string>? starterIds = null) => new()
        {
            SleeperUserId = SleeperUserId,
            SleeperRosterId = "101",
            SleeperLeagueId = LeagueId,
            TeamName = "Great Jeans",
            OwnerName = "Paul",
            PlayerIds = playerIds,
            StarterIds = starterIds ?? playerIds.Take(5).ToList(),
            Season = Season
        };

    private static GetStartSitRecommendationsQueryHandler BuildHandler(
        RosterPlayerDocument? rosterDoc,
        IEnumerable<SimulationResultDocument> sims,
        IEnumerable<Player> players,
        IEnumerable<InjuryAlertDocument>? injuries = null)
    {
        var rosterRepo = Substitute.For<IRosterPlayerRepository>();
        rosterRepo.GetBySleeperUserIdAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(rosterDoc);

        var playerRepo = Substitute.For<IPlayerRepository>();
        playerRepo.GetBySleeperIdsAsync(
                Arg.Any<IEnumerable<string>>(), Arg.Any<CancellationToken>())
            .Returns(players.ToList());

        var simRepo = Substitute.For<ISimulationResultRepository>();
        simRepo.GetLatestBySleeperIdsAsync(
                Arg.Any<IEnumerable<string>>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(sims.ToList().AsReadOnly() as IReadOnlyList<SimulationResultDocument>);

        var injuryRepo = Substitute.For<IInjuryAlertRepository>();
        injuryRepo.GetActiveAlertsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((injuries ?? []).ToList().AsReadOnly()
                as IReadOnlyList<InjuryAlertDocument>);

        return new GetStartSitRecommendationsQueryHandler(
            rosterRepo, playerRepo, simRepo, injuryRepo,
            NullLogger<GetStartSitRecommendationsQueryHandler>.Instance);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Returns_null_when_roster_not_found()
    {
        var handler = BuildHandler(null, [], []);
        var result = await handler.Handle(
            new GetStartSitRecommendationsQuery(SleeperUserId, LeagueId, Season, Week),
            CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Returns_empty_decisions_when_no_position_battles()
    {
        // Exactly 1 QB, 2 RB, 3 WR, 1 TE — no competition at any position
        var playerIds = new List<string>
            { "QB1", "RB1", "RB2", "WR1", "WR2", "WR3", "TE1" };

        var rosterDoc = MakeRosterDoc(playerIds, playerIds);
        var players = playerIds.Select(id => MakePlayer(id,
            id.StartsWith("QB") ? "QB" : id.StartsWith("RB") ? "RB" :
            id.StartsWith("WR") ? "WR" : "TE")).ToList();
        var sims = playerIds.Select(id => MakeSim(id,
            id.StartsWith("QB") ? "QB" : id.StartsWith("RB") ? "RB" :
            id.StartsWith("WR") ? "WR" : "TE", 14, 8, 22)).ToList();

        var handler = BuildHandler(rosterDoc, sims, players);
        var result = await handler.Handle(
            new GetStartSitRecommendationsQuery(SleeperUserId, LeagueId, Season, Week),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Decisions.Should().BeEmpty();
    }

    [Fact]
    public async Task Higher_median_player_gets_start_verdict()
    {
        // 3 RBs competing for 2 RB slots — RB1 and RB2 clearly ahead of RB3
        var playerIds = new List<string>
            { "QB1", "RB1", "RB2", "RB3", "WR1", "WR2", "WR3", "TE1" };

        var rosterDoc = MakeRosterDoc(playerIds);
        var players = playerIds.Select(id => MakePlayer(id,
            id.StartsWith("QB") ? "QB" : id.StartsWith("RB") ? "RB" :
            id.StartsWith("WR") ? "WR" : "TE")).ToList();

        var sims = new List<SimulationResultDocument>
        {
            MakeSim("QB1", "QB", 22, 14, 34),
            MakeSim("RB1", "RB", 22, 16, 32, boom: 0.35m, bust: 0.08m),  // clear starter
            MakeSim("RB2", "RB", 16, 11, 24, boom: 0.25m, bust: 0.12m),  // starter
            MakeSim("RB3", "RB",  8,  4, 14, boom: 0.10m, bust: 0.30m),  // clear bench
            MakeSim("WR1", "WR", 18, 12, 28),
            MakeSim("WR2", "WR", 14, 9,  22),
            MakeSim("WR3", "WR", 10, 6,  16),
            MakeSim("TE1", "TE", 12, 7,  20),
        };

        var handler = BuildHandler(rosterDoc, sims, players);
        var result = await handler.Handle(
            new GetStartSitRecommendationsQuery(SleeperUserId, LeagueId, Season, Week),
            CancellationToken.None);

        result.Should().NotBeNull();

        // Should have an RB decision
        var rbDecision = result!.Decisions.FirstOrDefault(d => d.Position == "RB");
        rbDecision.Should().NotBeNull();

        // Top option should be a Start or LeanStart
        var topOption = rbDecision!.Options.First();
        topOption.Verdict.Should().BeOneOf(
            StartSitVerdict.Start, StartSitVerdict.LeanStart);
    }

    [Fact]
    public async Task Lower_ranked_player_gets_sit_verdict()
    {
        var playerIds = new List<string>
            { "QB1", "RB1", "RB2", "RB3", "WR1", "WR2", "WR3", "TE1" };

        var rosterDoc = MakeRosterDoc(playerIds);
        var players = playerIds.Select(id => MakePlayer(id,
            id.StartsWith("QB") ? "QB" : id.StartsWith("RB") ? "RB" :
            id.StartsWith("WR") ? "WR" : "TE")).ToList();

        var sims = new List<SimulationResultDocument>
        {
            MakeSim("QB1", "QB", 22, 14, 34),
            MakeSim("RB1", "RB", 22, 16, 32),
            MakeSim("RB2", "RB", 16, 11, 24),
            MakeSim("RB3", "RB",  6,  3, 10),  // clear bench
            MakeSim("WR1", "WR", 18, 12, 28),
            MakeSim("WR2", "WR", 14,  9, 22),
            MakeSim("WR3", "WR", 10,  6, 16),
            MakeSim("TE1", "TE", 12,  7, 20),
        };

        var handler = BuildHandler(rosterDoc, sims, players);
        var result = await handler.Handle(
            new GetStartSitRecommendationsQuery(SleeperUserId, LeagueId, Season, Week),
            CancellationToken.None);

        result.Should().NotBeNull();
        var rbDecision = result!.Decisions.FirstOrDefault(d => d.Position == "RB");
        rbDecision.Should().NotBeNull();

        // Last option should be Sit or LeanSit
        var lastOption = rbDecision!.Options.Last();
        lastOption.Verdict.Should().BeOneOf(
            StartSitVerdict.Sit, StartSitVerdict.LeanSit);
    }

    [Fact]
    public async Task Injured_player_has_injury_designation_in_option()
    {
        var playerIds = new List<string>
            { "QB1", "RB1", "RB2", "RB3", "WR1", "WR2", "WR3", "TE1" };

        var rosterDoc = MakeRosterDoc(playerIds);
        var players = playerIds.Select(id => MakePlayer(id,
            id.StartsWith("QB") ? "QB" : id.StartsWith("RB") ? "RB" :
            id.StartsWith("WR") ? "WR" : "TE")).ToList();

        var sims = playerIds.Select(id => MakeSim(id,
            id.StartsWith("QB") ? "QB" : id.StartsWith("RB") ? "RB" :
            id.StartsWith("WR") ? "WR" : "TE", 14, 8, 22)).ToList();

        var injuries = new List<InjuryAlertDocument>
        {
            new() { SleeperPlayerId = "RB1", Designation = "Q", PlayerName = "Player RB1" }
        };

        var handler = BuildHandler(rosterDoc, sims, players, injuries);
        var result = await handler.Handle(
            new GetStartSitRecommendationsQuery(SleeperUserId, LeagueId, Season, Week),
            CancellationToken.None);

        result.Should().NotBeNull();

        var allOptions = result!.Decisions.SelectMany(d => d.Options).ToList();
        var rb1Option = allOptions.FirstOrDefault(o => o.SleeperPlayerId == "RB1");

        rb1Option.Should().NotBeNull();
        rb1Option!.InjuryDesignation.Should().Be("Q");
    }

    [Fact]
    public async Task IR_players_are_excluded_from_decisions()
    {
        // RB1 is on IR — should not appear in any decision
        var playerIds = new List<string>
            { "QB1", "RB1", "RB2", "RB3", "WR1", "WR2", "WR3", "TE1" };

        var rosterDoc = MakeRosterDoc(playerIds);
        var players = playerIds.Select(id => MakePlayer(id,
            id.StartsWith("QB") ? "QB" : id.StartsWith("RB") ? "RB" :
            id.StartsWith("WR") ? "WR" : "TE")).ToList();

        var sims = playerIds.Select(id => MakeSim(id,
            id.StartsWith("QB") ? "QB" : id.StartsWith("RB") ? "RB" :
            id.StartsWith("WR") ? "WR" : "TE", 14, 8, 22)).ToList();

        var injuries = new List<InjuryAlertDocument>
        {
            new() { SleeperPlayerId = "RB1", Designation = "IR", PlayerName = "Player RB1" }
        };

        var handler = BuildHandler(rosterDoc, sims, players, injuries);
        var result = await handler.Handle(
            new GetStartSitRecommendationsQuery(SleeperUserId, LeagueId, Season, Week),
            CancellationToken.None);

        result.Should().NotBeNull();

        var allOptions = result!.Decisions.SelectMany(d => d.Options).ToList();
        allOptions.Should().NotContain(o => o.SleeperPlayerId == "RB1");
    }

    [Fact]
    public async Task Flex_decision_is_generated_when_bubble_players_exist()
    {
        // Extra RB and WR create a FLEX battle
        var playerIds = new List<string>
            { "QB1", "RB1", "RB2", "RB3", "WR1", "WR2", "WR3", "WR4", "TE1" };

        var rosterDoc = MakeRosterDoc(playerIds);
        var players = playerIds.Select(id => MakePlayer(id,
            id.StartsWith("QB") ? "QB" : id.StartsWith("RB") ? "RB" :
            id.StartsWith("WR") ? "WR" : "TE")).ToList();

        var sims = new List<SimulationResultDocument>
        {
            MakeSim("QB1", "QB", 22, 14, 34),
            MakeSim("RB1", "RB", 22, 16, 32),
            MakeSim("RB2", "RB", 16, 11, 24),
            MakeSim("RB3", "RB", 12,  7, 18),  // flex bubble RB
            MakeSim("WR1", "WR", 18, 12, 28),
            MakeSim("WR2", "WR", 14,  9, 22),
            MakeSim("WR3", "WR", 10,  6, 16),  // last WR starter
            MakeSim("WR4", "WR",  9,  5, 14),  // WR bench / flex battle
            MakeSim("TE1", "TE", 12,  7, 20),
        };

        var handler = BuildHandler(rosterDoc, sims, players);
        var result = await handler.Handle(
            new GetStartSitRecommendationsQuery(SleeperUserId, LeagueId, Season, Week),
            CancellationToken.None);

        result.Should().NotBeNull();
        result!.Decisions.Should().Contain(d => d.Position == "FLEX");
    }

    [Fact]
    public async Task Each_option_has_non_empty_rationale()
    {
        var playerIds = new List<string>
            { "QB1", "RB1", "RB2", "RB3", "WR1", "WR2", "WR3", "TE1" };

        var rosterDoc = MakeRosterDoc(playerIds);
        var players = playerIds.Select(id => MakePlayer(id,
            id.StartsWith("QB") ? "QB" : id.StartsWith("RB") ? "RB" :
            id.StartsWith("WR") ? "WR" : "TE")).ToList();

        var sims = playerIds.Select(id => MakeSim(id,
            id.StartsWith("QB") ? "QB" : id.StartsWith("RB") ? "RB" :
            id.StartsWith("WR") ? "WR" : "TE", 14, 8, 22)).ToList();

        var handler = BuildHandler(rosterDoc, sims, players);
        var result = await handler.Handle(
            new GetStartSitRecommendationsQuery(SleeperUserId, LeagueId, Season, Week),
            CancellationToken.None);

        result.Should().NotBeNull();
        var allOptions = result!.Decisions.SelectMany(d => d.Options);
        allOptions.Should().OnlyContain(o => !string.IsNullOrWhiteSpace(o.Rationale));
    }
}
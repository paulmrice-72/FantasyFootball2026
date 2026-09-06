// FF.Tests/Application/Lineups/OptimizeMyLineupTests.cs
using FF.Application.Features.Lineups.Commands.OptimizeLineup;
using FF.Application.Interfaces.Persistence;
using FF.Application.Services.LineupOptimizer;
using FF.Domain.Documents;
using FF.Domain.Enums;
using FF.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FF.Tests.Application.Lineups;

public class OptimizeMyLineupTests
{
    // ── helpers ──────────────────────────────────────────────────────────────
    private static SimulationResultDocument MakeSim(
        string id, string sleeperPlayerId, string pos,
        decimal median, decimal floor, decimal ceiling) => new()
        {
            Id = id,
            PlayerId = id,
            SleeperPlayerId = sleeperPlayerId,
            PlayerName = $"Player {id}",
            Position = pos,
            NflTeam = "TST",
            Season = 2024,
            Week = 1,
            Floor = floor,
            Median = median,
            Ceiling = ceiling,
            BoomProbability = 0.30m,
            BustProbability = 0.10m,
            ScoringFormat = "HalfPpr",
            CalculatedAt = DateTime.UtcNow
        };

    /// <summary>
    /// Re-created by every BuildHandler call so a test can assert what the handler
    /// asked the league layer for. With no SleeperLeagueId on the command the handler
    /// must not touch it at all and falls back to RosterConfiguration.Standard —
    /// which is what every other assertion in this file was written against.
    /// </summary>
    private static ILeagueRepository _leagueRepo = Substitute.For<ILeagueRepository>();

    private static OptimizeLineupCommandHandler BuildHandler(
        IEnumerable<SimulationResultDocument> pool)
    {
        _leagueRepo = Substitute.For<ILeagueRepository>();
        var repo = Substitute.For<ISimulationResultRepository>();
        repo.GetByWeekAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(pool.ToList().AsReadOnly());
        return new OptimizeLineupCommandHandler(
            repo, _leagueRepo, NullLogger<OptimizeLineupCommandHandler>.Instance);
    }

    /// Full sim pool with 20 players — only 10 are "on the roster"
    private static (List<SimulationResultDocument> pool, List<string> rosterSleeperIds)
        BuildPoolWithRoster()
    {
        var pool = new List<SimulationResultDocument>
        {
            // Rostered players (SleeperPlayerId = "S" prefix)
            MakeSim("QB1",  "S-QB1",  "QB", 24, 18, 38),
            MakeSim("RB1",  "S-RB1",  "RB", 20, 16, 30),
            MakeSim("RB2",  "S-RB2",  "RB", 14, 10, 22),
            MakeSim("RB3",  "S-RB3",  "RB", 10,  6, 18),
            MakeSim("WR1",  "S-WR1",  "WR", 18, 14, 32),
            MakeSim("WR2",  "S-WR2",  "WR", 14, 10, 26),
            MakeSim("WR3",  "S-WR3",  "WR", 10,  6, 18),
            MakeSim("WR4",  "S-WR4",  "WR",  8,  4, 14),
            MakeSim("TE1",  "S-TE1",  "TE", 14, 10, 24),
            MakeSim("TE2",  "S-TE2",  "TE",  8,  4, 14),

            // Non-rostered (waiver wire / other teams) — must NOT appear in result
            MakeSim("QB99", "X-QB99", "QB", 40, 30, 55),   // elite QB not on roster
            MakeSim("RB99", "X-RB99", "RB", 35, 28, 50),   // elite RB not on roster
            MakeSim("WR99", "X-WR99", "WR", 32, 24, 48),   // elite WR not on roster
            MakeSim("TE99", "X-TE99", "TE", 30, 22, 45),   // elite TE not on roster
        };

        var rosterSleeperIds = pool
            .Where(p => p.SleeperPlayerId!.StartsWith("S-"))
            .Select(p => p.SleeperPlayerId!)
            .ToList();

        return (pool, rosterSleeperIds);
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Roster_filter_excludes_non_rostered_players_from_lineup()
    {
        // Even though QB99/RB99/WR99/TE99 are elite (much higher projections),
        // they are not on the roster and must NOT appear in the optimized lineup.
        var (pool, rosterSleeperIds) = BuildPoolWithRoster();
        var handler = BuildHandler(pool);

        var cmd = new OptimizeLineupCommand(
            Season: 2024,
            Week: 1,
            RosterSleeperIds: rosterSleeperIds);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Lineup.Should().NotContain(s => s.PlayerId == "QB99");
        result.Value.Lineup.Should().NotContain(s => s.PlayerId == "RB99");
        result.Value.Lineup.Should().NotContain(s => s.PlayerId == "WR99");
        result.Value.Lineup.Should().NotContain(s => s.PlayerId == "TE99");
    }

    [Fact]
    public async Task Roster_filter_produces_valid_starting_lineup_from_rostered_players()
    {
        var (pool, rosterSleeperIds) = BuildPoolWithRoster();
        var handler = BuildHandler(pool);

        var cmd = new OptimizeLineupCommand(
            Season: 2024,
            Week: 1,
            RosterSleeperIds: rosterSleeperIds);

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Lineup.Should().HaveCount(RosterConfiguration.Standard.TotalStarters);

        // All selected players must be rostered
        var selectedIds = result.Value.Lineup.Select(s => s.PlayerId).ToList();
        var rosterPlayerIds = pool
            .Where(p => rosterSleeperIds.Contains(p.SleeperPlayerId!))
            .Select(p => p.PlayerId)
            .ToHashSet();

        selectedIds.Should().OnlyContain(id => rosterPlayerIds.Contains(id));
    }

    [Fact]
    public async Task League_id_supplied_resolves_that_league_s_roster_configuration()
    {
        // The optimiser used to hardcode RosterConfiguration.Standard, which starts
        // two WRs. In a 3-WR league that silently produced a lineup one receiver short.
        var (pool, rosterSleeperIds) = BuildPoolWithRoster();
        var handler = BuildHandler(pool);

        var cmd = new OptimizeLineupCommand(
            Season: 2024,
            Week: 1,
            RosterSleeperIds: rosterSleeperIds,
            SleeperLeagueId: "league-123");

        await handler.Handle(cmd, CancellationToken.None);

        await _leagueRepo.Received(1)
            .GetBySleeperIdAsync("league-123", 2024, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_league_id_falls_back_to_standard_without_hitting_the_league_layer()
    {
        var (pool, rosterSleeperIds) = BuildPoolWithRoster();
        var handler = BuildHandler(pool);

        var cmd = new OptimizeLineupCommand(
            Season: 2024,
            Week: 1,
            RosterSleeperIds: rosterSleeperIds);   // no SleeperLeagueId

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Lineup.Should().HaveCount(RosterConfiguration.Standard.TotalStarters);

        await _leagueRepo.DidNotReceive()
            .GetBySleeperIdAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_roster_filter_uses_full_pool_as_before()
    {
        // Passing null RosterSleeperIds should behave exactly as pre-TEAM-002
        var (pool, _) = BuildPoolWithRoster();
        var handler = BuildHandler(pool);

        var cmd = new OptimizeLineupCommand(
            Season: 2024,
            Week: 1,
            RosterSleeperIds: null);   // no filter

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Elite non-rostered players ARE eligible — optimizer may pick them
        result.Value.Lineup.Should().HaveCount(RosterConfiguration.Standard.TotalStarters);
    }

    [Fact]
    public async Task Empty_roster_filter_returns_failure_not_exception()
    {
        // If roster SleeperIds are provided but none match sim data, graceful failure
        var (pool, _) = BuildPoolWithRoster();
        var handler = BuildHandler(pool);

        var cmd = new OptimizeLineupCommand(
            Season: 2024,
            Week: 1,
            RosterSleeperIds: ["GHOST-1", "GHOST-2"]);  // no matches in sim data

        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("Optimizer.NoRosterData");
    }
}
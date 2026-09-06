// FF.Tests/Features/Lineups/OptimizeLineupRiskProfileTests.cs
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

public class OptimizeLineupRiskProfileTests
{
    // ── helpers ────────────────────────────────────────────────────────────────

    private static SimulationResultDocument MakeSim(
        string id, string pos,
        decimal floor, decimal median, decimal ceiling,
        decimal boom, decimal bust,
        decimal? ownershipPct = null) =>
        new()
        {
            Id = id,
            PlayerId = id,
            PlayerName = $"Player {id}",
            Position = pos,
            NflTeam = "TST",
            Season = 2024,
            Week = 1,
            Floor = floor,
            Median = median,
            Ceiling = ceiling,
            BoomProbability = boom,
            BustProbability = bust,
            ScoringFormat = "HalfPpr",
            CalculatedAt = DateTime.UtcNow
        };

    /// <summary>
    /// Minimal valid pool: 1 QB, 2 RB, 3 WR, 1 TE — standard roster can fill 9 starters
    /// (1 QB + 2 RB + 3 WR + 1 TE + 1 FLEX = 8; we add extras so the solver has choices).
    /// </summary>
    private static List<SimulationResultDocument> BuildPool(
        SimulationResultDocument? extraQb = null,
        SimulationResultDocument? extraRb = null,
        SimulationResultDocument? extraWr = null)
    {
        var pool = new List<SimulationResultDocument>
        {
            // QBs
            MakeSim("QB1", "QB", 18, 24, 38, 0.40m, 0.05m),
            // RBs
            MakeSim("RB1", "RB", 16, 20, 30, 0.35m, 0.08m),
            MakeSim("RB2", "RB",  8, 12, 22, 0.20m, 0.20m),
            MakeSim("RB3", "RB",  6, 10, 18, 0.15m, 0.25m),
            // WRs
            MakeSim("WR1", "WR", 14, 18, 32, 0.38m, 0.10m),
            MakeSim("WR2", "WR", 10, 14, 26, 0.28m, 0.15m),
            MakeSim("WR3", "WR",  6, 10, 18, 0.15m, 0.25m),
            MakeSim("WR4", "WR",  4,  8, 14, 0.10m, 0.30m),
            // TEs
            MakeSim("TE1", "TE", 10, 14, 24, 0.30m, 0.12m),
            MakeSim("TE2", "TE",  4,  8, 14, 0.12m, 0.28m),
        };

        if (extraQb is not null) pool.Add(extraQb);
        if (extraRb is not null) pool.Add(extraRb);
        if (extraWr is not null) pool.Add(extraWr);

        return pool;
    }

    private static OptimizeLineupCommandHandler BuildHandler(
        IEnumerable<SimulationResultDocument> pool)
    {
        var repo = Substitute.For<ISimulationResultRepository>();
        repo.GetByWeekAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(pool.ToList().AsReadOnly());
        return new OptimizeLineupCommandHandler(
            repo, Substitute.For<ILeagueRepository>(),
            NullLogger<OptimizeLineupCommandHandler>.Instance);
    }

    // ── tests ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Safe_mode_selects_high_floor_and_penalises_bust()
    {
        // RB_HighFloor: floor 18, bust 0.05  → safe score = 18 - 0.5  = 17.5
        // RB_LowFloor:  floor  4, bust 0.40  → safe score =  4 - 4.0  =  0.0
        var highFloor = MakeSim("RB_HF", "RB", floor: 18, median: 20, ceiling: 26, boom: 0.30m, bust: 0.05m);
        var lowFloor = MakeSim("RB_LF", "RB", floor: 4, median: 18, ceiling: 30, boom: 0.45m, bust: 0.40m);

        var handler = BuildHandler(BuildPool(extraRb: highFloor).Append(lowFloor));

        var cmd = new OptimizeLineupCommand(2024, 1, RiskProfile: RiskProfile.Safe);
        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Lineup.Should().Contain(s => s.PlayerId == "RB_HF");
        result.Value.Lineup.Should().NotContain(s => s.PlayerId == "RB_LF");
    }

    [Fact]
    public async Task Ceiling_mode_selects_high_ceiling_and_rewards_boom()
    {
        // WR_HighCeil: ceiling 42, boom 0.55 → ceiling score = 42 + 5.5 = 47.5
        // WR_LowCeil:  ceiling 16, boom 0.10 → ceiling score = 16 + 1.0 = 17.0
        var highCeil = MakeSim("WR_HC", "WR", floor: 6, median: 18, ceiling: 42, boom: 0.55m, bust: 0.20m);
        var lowCeil = MakeSim("WR_LC", "WR", floor: 10, median: 14, ceiling: 16, boom: 0.10m, bust: 0.08m);

        var handler = BuildHandler(BuildPool(extraWr: highCeil).Append(lowCeil));

        var cmd = new OptimizeLineupCommand(2024, 1, RiskProfile: RiskProfile.Ceiling);
        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Lineup.Should().Contain(s => s.PlayerId == "WR_HC");
        result.Value.Lineup.Should().NotContain(s => s.PlayerId == "WR_LC");
    }

    [Fact]
    public async Task Contrarian_mode_penalises_high_ownership()
    {
        // Both WRs have equal ceiling but chalk has high ownership
        // WR_Diff:  ceiling 28, boom 0.40, ownership 10  → score = 28 + 3.2 - 5.0  = 26.2
        // WR_Chalk: ceiling 28, boom 0.40, ownership 60  → score = 28 + 3.2 - 30.0 =  1.2
        // Contrarian should prefer WR_Diff
        var diff = MakeSim("WR_DIFF", "WR", floor: 8, median: 16, ceiling: 28, boom: 0.40m, bust: 0.18m, ownershipPct: 10m);
        var chalk = MakeSim("WR_CHALK", "WR", floor: 8, median: 16, ceiling: 28, boom: 0.40m, bust: 0.18m, ownershipPct: 60m);

        // Need to carry OwnershipPct through — currently null in handler (pending DIFF-009).
        // For this test we go directly through LineupOptimizerService so we can set OwnershipPct.
        var players = new List<PlayerSlot>
        {
            new() { PlayerId="QB1",     Position="QB", NflTeam="T", ProjectedMedian=24, ProjectedFloor=18, ProjectedCeiling=38, BoomProbability=0.40m, BustProbability=0.05m },
            new() { PlayerId="RB1",     Position="RB", NflTeam="T", ProjectedMedian=20, ProjectedFloor=16, ProjectedCeiling=30, BoomProbability=0.35m, BustProbability=0.08m },
            new() { PlayerId="RB2",     Position="RB", NflTeam="T", ProjectedMedian=12, ProjectedFloor=8,  ProjectedCeiling=22, BoomProbability=0.20m, BustProbability=0.20m },
            new() { PlayerId="RB3",     Position="RB", NflTeam="T", ProjectedMedian=10, ProjectedFloor=6,  ProjectedCeiling=18, BoomProbability=0.15m, BustProbability=0.25m },
            new() { PlayerId="WR1",     Position="WR", NflTeam="T", ProjectedMedian=18, ProjectedFloor=14, ProjectedCeiling=32, BoomProbability=0.38m, BustProbability=0.10m },
            new() { PlayerId="WR2",     Position="WR", NflTeam="T", ProjectedMedian=14, ProjectedFloor=10, ProjectedCeiling=26, BoomProbability=0.28m, BustProbability=0.15m },
            new() { PlayerId="WR3",     Position="WR", NflTeam="T", ProjectedMedian=10, ProjectedFloor=6,  ProjectedCeiling=18, BoomProbability=0.15m, BustProbability=0.25m },
            new() { PlayerId="WR_DIFF",  Position="WR", NflTeam="T", ProjectedMedian=16, ProjectedFloor=8, ProjectedCeiling=28, BoomProbability=0.40m, BustProbability=0.18m, OwnershipPct=10m  },
            new() { PlayerId="WR_CHALK", Position="WR", NflTeam="T", ProjectedMedian=16, ProjectedFloor=8, ProjectedCeiling=28, BoomProbability=0.40m, BustProbability=0.18m, OwnershipPct=60m },
            new() { PlayerId="TE1",     Position="TE", NflTeam="T", ProjectedMedian=14, ProjectedFloor=10, ProjectedCeiling=24, BoomProbability=0.30m, BustProbability=0.12m },
        };

        var optimizerInput = new LineupOptimizerInput
        {
            AvailablePlayers = players,
            RosterConfig = RosterConfiguration.Standard,
            RiskProfile = RiskProfile.Contrarian,
            LockedPlayerIds = [],
            ExcludedPlayerIds = []
        };

        var result = LineupOptimizerService.Optimize(optimizerInput);

        result.Success.Should().BeTrue();
        result.Lineup.Should().Contain(s => s.PlayerId == "WR_DIFF");
        result.Lineup.Should().NotContain(s => s.PlayerId == "WR_CHALK");
    }

    [Fact]
    public async Task Null_risk_profile_falls_back_to_mode_scoring()
    {
        var handler = BuildHandler(BuildPool());

        // No RiskProfile — should behave identically to PBI-027 Median mode
        var cmd = new OptimizeLineupCommand(2024, 1, Mode: OptimizationMode.Median, RiskProfile: null);
        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RiskProfile.Should().BeNull();
        result.Value.Mode.Should().Be(OptimizationMode.Median);
        result.Value.Lineup.Should().HaveCount(RosterConfiguration.Standard.TotalStarters);
    }

    [Fact]
    public async Task Safe_mode_result_carries_risk_profile_on_response()
    {
        var handler = BuildHandler(BuildPool());

        var cmd = new OptimizeLineupCommand(2024, 1, RiskProfile: RiskProfile.Safe);
        var result = await handler.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.RiskProfile.Should().Be(RiskProfile.Safe);
    }
}
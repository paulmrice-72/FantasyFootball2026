// FF.Tests/Application/LineupOptimizerServiceTests.cs
using FF.Application.Services.LineupOptimizer;
using FF.Domain.ValueObjects;
using FluentAssertions;

namespace FF.Tests.Application;

public class LineupOptimizerServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static PlayerSlot Player(
        string id, string name, string position,
        decimal median = 15m, decimal floor = 8m, decimal ceiling = 25m) =>
        new()
        {
            PlayerId = id,
            PlayerName = name,
            Position = position,
            NflTeam = "KC",
            ProjectedMedian = median,
            ProjectedFloor = floor,
            ProjectedCeiling = ceiling
        };

    private static LineupOptimizerInput StandardInput(
        OptimizationMode mode = OptimizationMode.Median,
        IReadOnlyList<string>? locked = null,
        IReadOnlyList<string>? excluded = null)
    {
        var lockedIds = locked ?? [];
        var excludedIds = excluded ?? [];

        var players = new List<PlayerSlot>
    {
        Player("qb1", "Patrick Mahomes",     "QB", 32m, 22m, 48m),
        Player("qb2", "Josh Allen",          "QB", 28m, 18m, 42m),
        Player("rb1", "Christian McCaffrey", "RB", 28m, 18m, 40m),
        Player("rb2", "Austin Ekeler",       "RB", 20m, 12m, 30m),
        Player("rb3", "Tony Pollard",        "RB", 16m, 9m,  26m),
        Player("rb4", "Jahmyr Gibbs",        "RB", 18m, 10m, 28m),
        Player("wr1", "Tyreek Hill",         "WR", 26m, 16m, 40m),
        Player("wr2", "Stefon Diggs",        "WR", 22m, 14m, 34m),
        Player("wr3", "Davante Adams",       "WR", 20m, 12m, 32m),
        Player("wr4", "Justin Jefferson",    "WR", 24m, 15m, 38m),
        Player("te1", "Travis Kelce",        "TE", 22m, 12m, 36m),
        Player("te2", "Mark Andrews",        "TE", 18m, 10m, 28m),
        Player("te3", "Sam LaPorta",         "TE", 14m, 7m,  22m),
    }
        .Select(p => p with
        {
            IsLocked = lockedIds.Contains(p.PlayerId),
            IsExcluded = excludedIds.Contains(p.PlayerId)
        })
        .ToList();

        return new LineupOptimizerInput
        {
            RosterConfig = RosterConfiguration.Standard,
            Mode = mode,
            LockedPlayerIds = lockedIds,
            ExcludedPlayerIds = excludedIds,
            AvailablePlayers = players
        };
    }
    // ── Basic Validity Tests ──────────────────────────────────────────────

    [Fact]
    public void Optimize_ReturnsSuccess()
    {
        var result = LineupOptimizerService.Optimize(StandardInput());
        result.Success.Should().BeTrue();
    }

    [Fact]
    public void Optimize_LineupHasCorrectTotalStarters()
    {
        var result = LineupOptimizerService.Optimize(StandardInput());
        result.Lineup.Should().HaveCount(
            RosterConfiguration.Standard.TotalStarters,
            "lineup must fill exactly all starter slots");
    }

    [Fact]
    public void Optimize_LineupHasExactlyOneQB()
    {
        var result = LineupOptimizerService.Optimize(StandardInput());
        result.Lineup.Count(s => s.SlotType == "QB").Should().Be(1);
    }

    [Fact]
    public void Optimize_LineupHasCorrectRbCount()
    {
        var result = LineupOptimizerService.Optimize(StandardInput());
        result.Lineup.Count(s => s.SlotType == "RB").Should().Be(
            RosterConfiguration.Standard.RbSlots);
    }

    [Fact]
    public void Optimize_LineupHasCorrectWrCount()
    {
        var result = LineupOptimizerService.Optimize(StandardInput());
        result.Lineup.Count(s => s.SlotType == "WR").Should().Be(
            RosterConfiguration.Standard.WrSlots);
    }

    [Fact]
    public void Optimize_LineupHasExactlyOneTE()
    {
        var result = LineupOptimizerService.Optimize(StandardInput());
        result.Lineup.Count(s => s.SlotType == "TE").Should().Be(1);
    }

    [Fact]
    public void Optimize_LineupHasExactlyOneFlex()
    {
        var result = LineupOptimizerService.Optimize(StandardInput());
        result.Lineup.Count(s => s.SlotType == "FLEX").Should().Be(
            RosterConfiguration.Standard.FlexSlots);
    }

    [Fact]
    public void Optimize_FlexSlotIsEligiblePosition()
    {
        var result = LineupOptimizerService.Optimize(StandardInput());
        var flexPlayer = result.Lineup.Single(s => s.SlotType == "FLEX");
        flexPlayer.Position.Should().BeOneOf("RB", "WR", "TE",
            "FLEX must be filled by RB, WR, or TE");
    }

    [Fact]
    public void Optimize_NoDuplicatePlayers()
    {
        var result = LineupOptimizerService.Optimize(StandardInput());
        result.Lineup.Select(s => s.PlayerId).Should().OnlyHaveUniqueItems(
            "no player can appear twice in the same lineup");
    }

    [Fact]
    public void Optimize_TotalProjectedPointsMatchesSum()
    {
        var result = LineupOptimizerService.Optimize(StandardInput());
        var sum = Math.Round(result.Lineup.Sum(s => s.ProjectedPoints), 2);
        result.TotalProjectedPoints.Should().Be(sum);
    }

    // ── Optimization Mode Tests ───────────────────────────────────────────

    [Fact]
    public void Optimize_CeilingMode_SelectsHigherUpsidePlayers()
    {
        var median = LineupOptimizerService.Optimize(StandardInput(OptimizationMode.Median));
        var ceiling = LineupOptimizerService.Optimize(StandardInput(OptimizationMode.Ceiling));

        ceiling.TotalProjectedPoints.Should().BeGreaterThanOrEqualTo(
            median.TotalProjectedPoints,
            "ceiling mode optimizes for upside so total ceiling should be >= median total");
    }

    [Fact]
    public void Optimize_FloorMode_ProducesValidLineup()
    {
        var result = LineupOptimizerService.Optimize(StandardInput(OptimizationMode.Floor));
        result.Success.Should().BeTrue();
        result.Lineup.Should().HaveCount(RosterConfiguration.Standard.TotalStarters);
    }

    // ── Lock / Exclude Tests ──────────────────────────────────────────────

    [Fact]
    public void Optimize_LockedPlayer_AppearsInLineup()
    {
        var result = LineupOptimizerService.Optimize(
            StandardInput(locked: ["rb3"]));

        result.Lineup.Should().Contain(s => s.PlayerId == "rb3",
            "locked player must appear in the optimized lineup");
    }

    [Fact]
    public void Optimize_ExcludedPlayer_DoesNotAppearInLineup()
    {
        var result = LineupOptimizerService.Optimize(
            StandardInput(excluded: ["qb1"]));

        result.Lineup.Should().NotContain(s => s.PlayerId == "qb1",
            "excluded player must not appear in the optimized lineup");
    }

    [Fact]
    public void Optimize_ExcludedPlayer_SecondBestQBSelected()
    {
        // Exclude the top QB — optimizer should pick qb2
        var result = LineupOptimizerService.Optimize(
            StandardInput(excluded: ["qb1"]));

        result.Lineup.Should().Contain(s => s.PlayerId == "qb2" && s.SlotType == "QB");
    }

    // ── Edge Case Tests ───────────────────────────────────────────────────

    [Fact]
    public void Optimize_InsufficientPlayers_ReturnsFailed()
    {
        var input = new LineupOptimizerInput
        {
            RosterConfig = RosterConfiguration.Standard,
            AvailablePlayers = [Player("qb1", "Only QB", "QB")]
        };

        var result = LineupOptimizerService.Optimize(input);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Optimize_EmptyPlayerList_ReturnsFailed()
    {
        var input = new LineupOptimizerInput
        {
            RosterConfig = RosterConfiguration.Standard,
            AvailablePlayers = []
        };

        var result = LineupOptimizerService.Optimize(input);
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Be("No eligible players available.");
    }

    [Fact]
    public void Optimize_ModeStoredOnResult()
    {
        var result = LineupOptimizerService.Optimize(
            StandardInput(OptimizationMode.Ceiling));
        result.Mode.Should().Be(OptimizationMode.Ceiling);
    }
}
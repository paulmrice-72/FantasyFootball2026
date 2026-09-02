// FF.Tests/Application/Vorp/ReplacementLevelServiceTests.cs
using FF.Application.Services;
using FF.Domain.ValueObjects;
using FluentAssertions;

namespace FF.Tests.Application.Vorp;

/// <summary>
/// L3 replacement level — FAN-118.
///
/// The pools below are built with deliberately separated tiers (QBs well above RBs,
/// RBs just above WRs, TEs well below) so that greedy flex allocation has exactly one
/// correct answer and the assertions are arithmetic rather than approximate.
/// </summary>
public class ReplacementLevelServiceTests
{
    private const int Teams = 12;

    // Descending, evenly spaced, exact in decimal.
    private static List<ReplacementCandidate> Pool(
        string position, int count, decimal top, bool rostered = true) =>
        Enumerable.Range(0, count)
            .Select(i => new ReplacementCandidate(
                PlayerId:        $"{position}{i}",
                Position:        position,
                ProjectedPoints: top - 0.1m * i,
                IsRostered:      rostered))
            .ToList();

    /// <summary>
    /// Tiers are separated widely enough that the flex allocation has exactly one
    /// answer over the full run of twelve slots.
    ///
    /// <para>
    /// An earlier version of this fixture had RB topping at 20 and WR at 19, which is
    /// NOT far enough apart: the pools converge as the RB pointer advances, so
    /// RB[34] = 16.6 ties WR[24] = 16.6 and WR takes the twelfth flex slot. The
    /// allocator was right to interleave — the fixture was simply too tight to express
    /// "RB dominates the flex". WR now tops at 15, so RB[35] = 16.5 still clears
    /// WR[24] = 12.6 and the intent holds all the way through.
    /// </para>
    /// </summary>
    private static List<ReplacementCandidate> StandardPool() =>
    [
        .. Pool("QB", 40, 30m),
        .. Pool("RB", 60, 20m),
        .. Pool("WR", 80, 15m),
        .. Pool("TE", 30, 10m)
    ];

    private static RosterConfiguration OneQb => new()
    {
        QbSlots = 1, RbSlots = 2, WrSlots = 2, TeSlots = 1,
        FlexSlotDefinitions = [new FlexSlotDefinition(["RB", "WR", "TE"])]
    };

    private static RosterConfiguration Superflex => new()
    {
        QbSlots = 1, RbSlots = 2, WrSlots = 2, TeSlots = 1,
        FlexSlotDefinitions =
        [
            new FlexSlotDefinition(["RB", "WR", "TE"]),
            new FlexSlotDefinition(["QB", "RB", "WR", "TE"])
        ]
    };

    // ── The off-by-one that FAN-118 replaces ─────────────────────────────────
    // The old handler used ranked[slotCount - 1] — the WORST STARTER. Replacement
    // is the first player who would NOT start: index 12, not index 11.

    [Fact]
    public void Structural_IsFirstNonStarter_NotWorstStarter()
    {
        var levels = ReplacementLevelService.Compute(StandardPool(), OneQb, Teams);

        // 12 teams x 1 QB = 12 starters; no flex accepts QB, so the 13th QB (index 12)
        // is the replacement. 30 - 0.1*12 = 28.8. The worst starter would be 28.9.
        levels["QB"].StartersAbsorbed.Should().Be(12);
        levels["QB"].StructuralLevel.Should().Be(28.8m);
    }

    // ── Superflex is the whole point of "league-aware" ───────────────────────

    [Fact]
    public void Superflex_PushesQbReplacementDeeper_ThanOneQb()
    {
        var oneQb     = ReplacementLevelService.Compute(StandardPool(), OneQb, Teams);
        var superflex = ReplacementLevelService.Compute(StandardPool(), Superflex, Teams);

        // Every superflex slot goes to a QB here: QB[12] = 28.8 outscores RB[24] = 17.6.
        // 12 base + 12 superflex = 24 QBs started, so replacement is the 25th (index 24).
        superflex["QB"].StartersAbsorbed.Should().Be(24);
        superflex["QB"].StructuralLevel.Should().Be(27.6m);

        superflex["QB"].StructuralLevel
            .Should().BeLessThan(oneQb["QB"].StructuralLevel,
                "a superflex league starts twice the quarterbacks, so replacement sits deeper "
                + "and elite QBs are worth correspondingly more");
    }

    [Fact]
    public void Superflex_DoesNotConsumeTheRbWrTeFlexWithAQuarterback()
    {
        var levels = ReplacementLevelService.Compute(StandardPool(), Superflex, Teams);

        // The RB/WR/TE flex cannot take a QB, so RBs still win all 12 of those:
        // 24 base + 12 flex = 36, replacement is index 36 → 20 - 3.6 = 16.4.
        levels["RB"].StartersAbsorbed.Should().Be(36);
        levels["RB"].StructuralLevel.Should().Be(16.4m);
    }

    // ── Greedy allocation ────────────────────────────────────────────────────

    [Fact]
    public void Flex_GoesToTheHighestScoringEligiblePosition()
    {
        var levels = ReplacementLevelService.Compute(StandardPool(), OneQb, Teams);

        // RB stays ahead for all twelve picks: RB[24] = 17.6 down to RB[35] = 16.5,
        // against WR[24] = 12.6 and TE[12] = 8.8.
        levels["RB"].StartersAbsorbed.Should().Be(36);
        levels["WR"].StartersAbsorbed.Should().Be(24);
        levels["TE"].StartersAbsorbed.Should().Be(12);
    }

    [Fact]
    public void Flex_SwitchesPosition_WhenTheOtherPoolIsStronger()
    {
        // Same shape, tiers swapped — the allocation must follow the players, not a
        // hardcoded RB/WR convention.
        List<ReplacementCandidate> pool =
        [
            .. Pool("QB", 40, 30m),
            .. Pool("RB", 60, 15m),
            .. Pool("WR", 80, 20m),
            .. Pool("TE", 30, 10m)
        ];

        var levels = ReplacementLevelService.Compute(pool, OneQb, Teams);

        levels["WR"].StartersAbsorbed.Should().Be(36);
        levels["RB"].StartersAbsorbed.Should().Be(24);
    }

    // ── Shallow pools are flagged, not zeroed ────────────────────────────────

    [Fact]
    public void ExhaustedPool_FlagsAndFallsBackToLastProjection_RatherThanZero()
    {
        // Only 5 TEs exist but the league starts 12.
        List<ReplacementCandidate> pool =
        [
            .. Pool("QB", 40, 30m),
            .. Pool("RB", 60, 20m),
            .. Pool("WR", 80, 19m),
            .. Pool("TE", 5, 10m)
        ];

        var levels = ReplacementLevelService.Compute(pool, OneQb, Teams);

        levels["TE"].PoolExhausted.Should().BeTrue();
        levels["TE"].StructuralLevel.Should().Be(9.6m, "the last real TE projection, not 0");
        levels["TE"].StructuralLevel.Should().NotBe(0m,
            "a fabricated zero would silently inflate every TE's VORP");
    }

    // ── Free-agent baseline, leave-one-out ───────────────────────────────────

    [Fact]
    public void FreeAgentBaseline_IgnoresRosteredPlayers()
    {
        var pool = StandardPool();
        // Free the 6th and 8th best RBs; everyone else stays rostered.
        pool = pool.Select(c => c.PlayerId is "RB5" or "RB7"
            ? c with { IsRostered = false } : c).ToList();

        var levels = ReplacementLevelService.Compute(pool, OneQb, Teams);

        levels["RB"].FreeAgentBestPlayerId.Should().Be("RB5");
        levels["RB"].FreeAgentBest.Should().Be(19.5m);
        levels["RB"].FreeAgentSecondBest.Should().Be(19.3m);
    }

    [Fact]
    public void FreeAgentVorp_ScoresTheBestFreeAgentAgainstTheSecond_NotHimself()
    {
        var pool = StandardPool();
        pool = pool.Select(c => c.PlayerId is "RB5" or "RB7"
            ? c with { IsRostered = false } : c).ToList();

        var levels = ReplacementLevelService.Compute(pool, OneQb, Teams);
        var best   = pool.First(c => c.PlayerId == "RB5");

        var vorp = ReplacementLevelService.FreeAgentVorp(best, levels);

        // 19.5 - 19.3. Measured against himself this would be exactly 0, which would
        // bury the single most valuable pickup on the board.
        vorp.Should().Be(0.2m);
        vorp.Should().NotBe(0m);
    }

    [Fact]
    public void FreeAgentVorp_IsNull_WhenNobodyAtThePositionIsAvailable()
    {
        var levels = ReplacementLevelService.Compute(StandardPool(), OneQb, Teams);
        var anyRb  = StandardPool().First(c => c.Position == "RB");

        ReplacementLevelService.FreeAgentVorp(anyRb, levels)
            .Should().BeNull("an empty wire is not the same as a wire worth zero points");
    }

    // ── Guards ───────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_Throws_WhenTeamCountIsNotPositive()
    {
        var act = () => ReplacementLevelService.Compute(StandardPool(), OneQb, 0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Compute_IsDeterministic_ForTiedProjections()
    {
        List<ReplacementCandidate> tied =
        [
            .. Enumerable.Range(0, 40).Select(i =>
                new ReplacementCandidate($"QB{i:D2}", "QB", 20m, true)),
            .. Pool("RB", 60, 20m),
            .. Pool("WR", 80, 19m),
            .. Pool("TE", 30, 10m)
        ];

        var first  = ReplacementLevelService.Compute(tied, OneQb, Teams);
        var second = ReplacementLevelService.Compute(tied, OneQb, Teams);

        first["QB"].StructuralLevel.Should().Be(second["QB"].StructuralLevel);
        first["QB"].FreeAgentBestPlayerId.Should().Be(second["QB"].FreeAgentBestPlayerId);
    }
}

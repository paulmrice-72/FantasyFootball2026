using FF.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FF.Tests.Application.Leagues;

/// <summary>
/// Tests for the draft board's roster-need model.
///
/// The headline case is <see cref="BestFit_HasSomethingToSay_WhenTheLineupIsFull"/>
/// and its sibling: until 2026-09-07 the only need term was the starter gap, so
/// once every starting slot was filled — round six or so of every draft — the
/// whole model returned zero for every player and "Best Fit (Value + Roster
/// Need)" silently became a copy of the Best Player Available list above it.
/// These pin the behaviour that ended that.
/// </summary>
public class RosterNeedModelTests
{
    /// <summary>
    /// Paul's actual league: QB1 / RB2 / WR2 / TE1 + 2 × W/R flex, plus the K and
    /// DEF the roster config could not previously express at all.
    /// </summary>
    private static RosterConfiguration PaulsLeague => new()
    {
        QbSlots = 1,
        RbSlots = 2,
        WrSlots = 2,
        TeSlots = 1,
        KSlots = 1,
        DefSlots = 1,
        FlexSlotDefinitions =
        [
            new FlexSlotDefinition(["RB", "WR"]),
            new FlexSlotDefinition(["RB", "WR"])
        ]
    };

    /// <summary>The roster Paul finished his 2026 draft with, by position.</summary>
    private static Dictionary<string, int> PaulsRoster => new(StringComparer.OrdinalIgnoreCase)
    {
        ["QB"] = 2,   // Allen, Murray
        ["RB"] = 4,   // Walker, Javonte, Stevenson, Mason
        ["WR"] = 6,   // Nacua, G.Wilson, Moore, Burden, Watson, M.Wilson
        ["TE"] = 1,   // Fannin
    };

    private static Dictionary<string, int> Counts(params (string Pos, int N)[] entries) =>
        entries.ToDictionary(e => e.Pos, e => e.N, StringComparer.OrdinalIgnoreCase);

    // ── Lineup simulation ────────────────────────────────────────────────────

    [Fact]
    public void EmptyRoster_HasAGapAtEverySkillPosition()
    {
        var model = new RosterNeedModel(PaulsLeague);
        var empty = Counts();

        // Two dedicated WR slots plus two W/R flex slots a receiver can claim,
        // capped at the three-deep gap ceiling.
        model.StarterGap("WR", empty).Should().Be(3);
        model.StarterGap("RB", empty).Should().Be(3);

        // A tight end converts once and stops: after the TE slot there is no
        // slot in this league he is eligible for. This is the case a single
        // undifferentiated FLEX count could not express.
        model.StarterGap("TE", empty).Should().Be(1);
        model.StarterGap("QB", empty).Should().Be(1);
    }

    [Fact]
    public void KickerAndDefense_NeverEarnStarterGapCredit()
    {
        var model = new RosterNeedModel(PaulsLeague);
        var empty = Counts();

        // Arithmetically an empty K slot IS an unfilled starting slot, and the
        // lineup simulation says so. It is excluded from gap credit on purpose:
        // crediting it would rank a kicker above real players from pick one.
        model.UnfillableStarterSlots(empty).Should().Be(10);

        model.StarterGapWeight("K", empty).Should().Be(0);
        model.StarterGapWeight("DEF", empty).Should().Be(0);
        model.DepthWeight("K", empty).Should().Be(0);
        model.DepthWeight("DEF", empty).Should().Be(0);
    }

    [Fact]
    public void FlexSlots_AreNotUsedUpByAPositionABroaderSlotCouldHaveTaken()
    {
        // FLEX (RB/WR/TE) plus W/R (RB/WR). A roster of 1 TE surplus and 1 RB
        // surplus must fill both slots: the TE has to take the broad FLEX,
        // leaving the RB for the W/R. Filling most-restrictive-first is what
        // makes that come out right.
        var config = new RosterConfiguration
        {
            QbSlots = 0,
            RbSlots = 0,
            WrSlots = 0,
            TeSlots = 0,
            FlexSlotDefinitions =
            [
                new FlexSlotDefinition(["RB", "WR", "TE"]),
                new FlexSlotDefinition(["RB", "WR"])
            ]
        };

        var model = new RosterNeedModel(config);

        model.CountFilledStarters(Counts(("TE", 1), ("RB", 1))).Should().Be(2);

        // Two tight ends can only ever fill the one slot they are eligible for.
        model.CountFilledStarters(Counts(("TE", 2))).Should().Be(1);
    }

    // ── The headline regression ──────────────────────────────────────────────

    [Fact]
    public void BestFit_HasSomethingToSay_WhenTheLineupIsFull()
    {
        var model = new RosterNeedModel(PaulsLeague);
        var counts = PaulsRoster;

        // Every skill slot is filled — this is the state in which the old
        // single-term model went to zero across the board and Best Fit silently
        // became Best Player Available.
        model.StarterGap("WR", counts).Should().Be(0);
        model.StarterGap("RB", counts).Should().Be(0);
        model.StarterGap("TE", counts).Should().Be(0);
        model.StarterGap("QB", counts).Should().Be(0);

        // A seventh receiver is a bench body: six WRs already cover two WR slots
        // and both flex slots with surplus to spare.
        var wr = model.Assess("WR", counts, picksRemaining: 6);
        wr.Bonus.Should().Be(0);

        // A second tight end is not. One TE fills the one TE slot and there is
        // nobody behind him — an injury there starts nobody.
        var te = model.Assess("TE", counts, picksRemaining: 6);
        te.Bonus.Should().BeGreaterThan(0);
        te.Reason.Should().Contain("behind the starter");

        // Which is the whole point: the model now separates them.
        te.Bonus.Should().BeGreaterThan(wr.Bonus);
    }

    [Fact]
    public void DepthWeight_ScalesWithHowExposedThePositionIs()
    {
        var model = new RosterNeedModel(PaulsLeague);

        // 1 TE for 1 TE slot — nobody behind him.
        model.DepthWeight("TE", Counts(("TE", 1))).Should().Be(1.0);

        // 2 TEs — one backup.
        model.DepthWeight("TE", Counts(("TE", 2))).Should().Be(0.4);

        // 3 TEs — covered.
        model.DepthWeight("TE", Counts(("TE", 3))).Should().Be(0.0);

        // A position the roster does not start yet has no depth exposure; that
        // is the starter gap's business, not this term's.
        model.DepthWeight("TE", Counts()).Should().Be(0.0);
    }

    // ── Urgency ──────────────────────────────────────────────────────────────

    [Fact]
    public void Urgency_IsSilentWhileThereArePicksToSpare()
    {
        var model = new RosterNeedModel(PaulsLeague);
        var counts = PaulsRoster;   // K and DEF both unfilled → 2 unfillable slots

        model.UnfillableStarterSlots(counts).Should().Be(2);

        // Six picks left against two holes: four picks of slack. Nothing yet —
        // you should still be drafting the best player available.
        model.MandatoryUrgency("K", counts, picksRemaining: 6).Should().Be(0);
        model.MandatoryUrgency("K", counts, picksRemaining: 5).Should().Be(0);
    }

    [Theory]
    [InlineData(5, 0)]      // slack 3 — still silent
    [InlineData(4, 20)]     // slack 2
    [InlineData(3, 40)]     // slack 1
    [InlineData(2, 60)]     // slack 0 — every remaining pick is spoken for
    public void Urgency_RampsAsThePicksRunOut(int picksRemaining, double expected)
    {
        var model = new RosterNeedModel(PaulsLeague);

        model.MandatoryUrgency("K", PaulsRoster, picksRemaining)
             .Should().BeApproximately(expected, 0.001);
    }

    [Fact]
    public void Urgency_OutranksEverythingElseAtZeroSlack()
    {
        var model = new RosterNeedModel(PaulsLeague);
        var counts = PaulsRoster;

        // Two picks, two mandatory holes. A kicker must now beat a tight end
        // whose only claim is thin depth — because the TE is optional and the
        // kicker slot will otherwise score zero every week.
        var k = model.Assess("K", counts, picksRemaining: 2);
        var te = model.Assess("TE", counts, picksRemaining: 2);

        k.Bonus.Should().BeGreaterThan(te.Bonus);
        k.Reason.Should().Be("K required · 2 picks left");
    }

    [Fact]
    public void Urgency_StaysSilentWithoutALivePickQueue()
    {
        var model = new RosterNeedModel(PaulsLeague);

        // picksRemaining == 0 means "we do not know", not "you have no picks".
        // Inventing a deadline from a missing number is exactly the kind of
        // confident wrong answer this model exists to stop making.
        model.MandatoryUrgency("K", PaulsRoster, picksRemaining: 0).Should().Be(0);
    }

    [Fact]
    public void Urgency_AppliesToAnyRequiredPosition_NotJustKickers()
    {
        var model = new RosterNeedModel(PaulsLeague);

        // No tight end at all, two picks left, K and DEF also missing.
        var counts = Counts(("QB", 2), ("RB", 4), ("WR", 6));

        model.MandatoryUrgency("TE", counts, picksRemaining: 2)
             .Should().BeGreaterThan(0);
    }

    [Fact]
    public void UrgencyGoesQuiet_OnceTheSlotIsFilled()
    {
        var model = new RosterNeedModel(PaulsLeague);

        var withKicker = new Dictionary<string, int>(PaulsRoster, StringComparer.OrdinalIgnoreCase)
        {
            ["K"] = 1
        };

        model.MandatoryUrgency("K", withKicker, picksRemaining: 2).Should().Be(0);
        model.MandatoryUrgency("DEF", withKicker, picksRemaining: 2).Should().BeGreaterThan(0);
    }
}

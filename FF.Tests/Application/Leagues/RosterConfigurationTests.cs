using FF.Domain.ValueObjects;
using FluentAssertions;
using Xunit;

namespace FF.Tests.Application.Leagues;

/// <summary>
/// Tests for parsing a Sleeper roster_positions array.
///
/// This type existed since April and, until 2026-09-07, had exactly one caller
/// (League.GetRosterConfiguration) and no tests — which is how it came to drop
/// kickers and defenses entirely, model a receiver flex as running-back
/// eligible, and invent a flex slot for leagues that do not play one.
/// </summary>
public class RosterConfigurationTests
{
    [Fact]
    public void ParsesKickerAndDefenseSlots()
    {
        // 2026-09-07: K and DEF were never counted here at all. A league
        // requiring both had two mandatory starting slots that were invisible to
        // everything downstream — including the draft board, which therefore told
        // Paul his roster was complete while he had neither.
        var config = RosterConfiguration.FromSleeperPositions(
            ["QB", "RB", "RB", "WR", "WR", "TE", "WRRB_FLEX", "WRRB_FLEX",
             "K", "DEF", "BN", "BN", "BN", "BN", "BN", "BN"]);

        config.KSlots.Should().Be(1);
        config.DefSlots.Should().Be(1);
        config.BenchSlots.Should().Be(6);

        // Two numbers, deliberately. TotalStarters is what the lineup optimizer
        // models and hard-constrains its solver against; it must stay at the
        // eight QB/RB/WR/TE/flex slots or every optimisation for this league
        // becomes infeasible. TotalStartingSlots is what the league actually
        // plays on Sunday.
        config.TotalStarters.Should().Be(8);
        config.TotalStartingSlots.Should().Be(10);
    }

    [Theory]
    [InlineData("DEF")]
    [InlineData("DST")]
    [InlineData("D")]
    public void AcceptsEverySpellingOfATeamDefense(string token)
    {
        // Sleeper says DEF, FFC said DST, some feeds say D. A spelling
        // difference must not cost the roster a starting slot.
        RosterConfiguration.FromSleeperPositions(["QB", token]).DefSlots.Should().Be(1);
    }

    [Fact]
    public void RecFlex_IsReceiverOnly()
    {
        // 2026-09-07 fix: REC_FLEX was being built as ["RB","WR","TE"] — the same
        // eligibility as a plain FLEX. Sleeper's REC_FLEX is WR or TE, no running
        // back. Modelling it as RB-eligible credits running backs with a slot
        // they can never occupy, inflating RB need in precisely the leagues that
        // chose this slot to do the opposite.
        var config = RosterConfiguration.FromSleeperPositions(["REC_FLEX"]);

        var slot = config.FlexSlotDefinitions.Should().ContainSingle().Subject;

        slot.IsEligible("WR").Should().BeTrue();
        slot.IsEligible("TE").Should().BeTrue();
        slot.IsEligible("RB").Should().BeFalse();
    }

    [Fact]
    public void SuperFlexAndWrRbFlex_KeepTheirOwnEligibility()
    {
        var config = RosterConfiguration.FromSleeperPositions(
            ["SUPER_FLEX", "WRRB_FLEX"]);

        var superFlex = config.FlexSlotDefinitions
            .Should().ContainSingle(f => f.IsEligible("QB")).Subject;
        superFlex.IsEligible("TE").Should().BeTrue();

        var wrrb = config.FlexSlotDefinitions
            .Should().ContainSingle(f => !f.IsEligible("QB")).Subject;
        wrrb.IsEligible("RB").Should().BeTrue();
        wrrb.IsEligible("WR").Should().BeTrue();
        wrrb.IsEligible("TE").Should().BeFalse();
    }

    [Fact]
    public void DoesNotInventAFlexSlotForALeagueThatPlaysNone()
    {
        // The old code ran `if (flexSlots.Count == 0) flexSlots.Add(RB/WR/TE)`,
        // fabricating a starting slot for any league without one and reporting
        // one more starter than the league actually plays.
        var config = RosterConfiguration.FromSleeperPositions(
            ["QB", "RB", "RB", "WR", "WR", "TE", "K", "DEF", "BN", "BN"]);

        config.FlexSlotDefinitions.Should().BeEmpty();
        config.TotalStarters.Should().Be(6);        // QB1 RB2 WR2 TE1, no flex
        config.TotalStartingSlots.Should().Be(8);   // + K + DEF
    }

    [Fact]
    public void UnrecognisedStartingSlots_AreReportedRatherThanDropped()
    {
        // IDP. This board cannot model it, but a slot silently ignored makes the
        // roster look more complete than it is — the same class of quiet wrong
        // answer that losing K and DEF produced.
        var config = RosterConfiguration.FromSleeperPositions(
            ["QB", "RB", "WR", "TE", "DL", "LB", "DB", "BN"]);

        config.UnsupportedSlots.Should().BeEquivalentTo(["DL", "LB", "DB"]);
        config.TotalStarters.Should().Be(4);        // QB/RB/WR/TE only
        config.TotalStartingSlots.Should().Be(7);   // + 3 unmodelled
    }

    [Fact]
    public void BenchIrAndTaxi_AreNotStartingSlots()
    {
        var config = RosterConfiguration.FromSleeperPositions(
            ["QB", "BN", "BN", "IR", "TAXI"]);

        config.TotalStarters.Should().Be(1);
        config.TotalStartingSlots.Should().Be(1);
        config.UnsupportedSlots.Should().BeEmpty();
    }

    [Fact]
    public void RoundTripsBackToTheSleeperVocabulary()
    {
        string[] original =
            ["QB", "RB", "RB", "WR", "WR", "TE", "WRRB_FLEX", "WRRB_FLEX", "K", "DEF"];

        var round = RosterConfiguration
            .FromSleeperPositions(original)
            .ToSleeperPositions();

        round.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void LeagueWithNoSyncedPositions_FallsBackToStandard()
    {
        // Standard now carries the K and DEF that nearly every redraft league
        // actually plays, so the draft board's fallback stops claiming a lineup
        // is complete when two required slots are empty.
        RosterConfiguration.Standard.KSlots.Should().Be(1);
        RosterConfiguration.Standard.DefSlots.Should().Be(1);

        // Unchanged at 7, and it must stay that way: LineupOptimizerServiceTests
        // asserts the optimizer fills exactly Standard.TotalStarters slots, and
        // the solver has no kicker or defense variable to fill.
        RosterConfiguration.Standard.TotalStarters.Should().Be(7);
        RosterConfiguration.Standard.TotalStartingSlots.Should().Be(9);
    }
}

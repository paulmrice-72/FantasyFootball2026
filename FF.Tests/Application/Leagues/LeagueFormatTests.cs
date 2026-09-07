using FF.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FF.Tests.Application.Leagues;

/// <summary>
/// Tests for the three-valued league format.
///
/// "Keeper" was reachable from league import since the beginning and read by
/// nothing. The two places that branched on it each assumed two values and
/// picked opposite defaults, so a keeper league was redraft on the draft board
/// and dynasty on the team page at the same time. These pin the behaviour that
/// replaced the guessing.
/// </summary>
public class LeagueFormatTests
{
    [Theory]
    [InlineData(0, LeagueFormat.Redraft)]
    [InlineData(1, LeagueFormat.Keeper)]
    [InlineData(2, LeagueFormat.Dynasty)]
    public void MapsSleeperSettingsType(int sleeperType, LeagueFormat expected) =>
        LeagueFormatExtensions.FromSleeperType(sleeperType).Should().Be(expected);

    [Fact]
    public void UnknownSleeperType_FallsBackToRedraft() =>
        LeagueFormatExtensions.FromSleeperType(99).Should().Be(LeagueFormat.Redraft);

    [Theory]
    [InlineData("Dynasty", LeagueFormat.Dynasty)]
    [InlineData("Keeper", LeagueFormat.Keeper)]
    [InlineData("Redraft", LeagueFormat.Redraft)]
    [InlineData("dynasty", LeagueFormat.Dynasty)]
    [InlineData("  KEEPER  ", LeagueFormat.Keeper)]
    public void ParsesStoredLeagueType(string stored, LeagueFormat expected) =>
        LeagueFormatExtensions.ParseLeagueFormat(stored).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Something New Sleeper Added")]
    public void UnknownOrMissingStoredType_FallsBackToRedraft(string? stored) =>
        LeagueFormatExtensions.ParseLeagueFormat(stored).Should().Be(LeagueFormat.Redraft);

    [Theory]
    [InlineData(LeagueFormat.Redraft)]
    [InlineData(LeagueFormat.Keeper)]
    [InlineData(LeagueFormat.Dynasty)]
    public void StorageStringRoundTrips(LeagueFormat format) =>
        LeagueFormatExtensions.ParseLeagueFormat(format.ToStorageString())
            .Should().Be(format);

    // ── Capabilities ─────────────────────────────────────────────────────────

    [Fact]
    public void OnlyDynastyDraftsFromTheRookiePool()
    {
        LeagueFormat.Dynasty.UsesRookieDraftPool().Should().BeTrue();

        // A keeper league drafts the whole player pool minus keepers, so its
        // draft board is the redraft board. This is the half the old
        // `LeagueType != "Dynasty"` test happened to get right.
        LeagueFormat.Keeper.UsesRookieDraftPool().Should().BeFalse();
        LeagueFormat.Redraft.UsesRookieDraftPool().Should().BeFalse();
    }

    [Fact]
    public void KeeperAndDynastyBothOwnTradeablePicks()
    {
        LeagueFormat.Dynasty.HasTradeablePicks().Should().BeTrue();
        LeagueFormat.Keeper.HasTradeablePicks().Should().BeTrue();
        LeagueFormat.Redraft.HasTradeablePicks().Should().BeFalse();
    }

    [Fact]
    public void OnlyDynastyCountsTheCarriedRosterTowardDraftNeeds()
    {
        // The rule that cost Paul a kicker on 2026-09-06.
        //
        // Sleeper reports your live roster for the league. In a dynasty league
        // that is your real team and a rookie draft adds to it. In a KEEPER
        // league before rollover it is still last season's entire roster — his
        // carried a kicker and a defense he was not keeping, which made the
        // board treat both mandatory slots as filled and return zero urgency
        // for a kicker he did not have, one pick from the end of the draft.
        LeagueFormat.Dynasty.CarriedRosterCountsTowardDraftNeeds().Should().BeTrue();
        LeagueFormat.Keeper.CarriedRosterCountsTowardDraftNeeds().Should().BeFalse();
        LeagueFormat.Redraft.CarriedRosterCountsTowardDraftNeeds().Should().BeFalse();
    }

    [Fact]
    public void KeeperIsNeitherOfTheOldTwoDefaults()
    {
        // The regression this whole type exists to prevent. The two consumers
        // tested opposite ways, so a keeper league satisfied BOTH "not dynasty"
        // and "not redraft" and was treated as each in turn.
        const LeagueFormat keeper = LeagueFormat.Keeper;

        keeper.Should().NotBe(LeagueFormat.Dynasty);
        keeper.Should().NotBe(LeagueFormat.Redraft);

        // And it now answers each capability deliberately rather than by default.
        keeper.UsesRookieDraftPool().Should().BeFalse();          // like redraft
        keeper.HasTradeablePicks().Should().BeTrue();             // like dynasty
        keeper.CarriedRosterCountsTowardDraftNeeds().Should().BeFalse();
    }
}

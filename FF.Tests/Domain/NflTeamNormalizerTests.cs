using FF.Domain.Services;
using FluentAssertions;
using Xunit;

// Namespace is FF.Tests.Services, not FF.Tests.Domain, deliberately.
//
// Declaring FF.Tests.Domain makes it shadow FF.Domain for any file that writes a
// fully-qualified FF.Domain.Something from inside an FF.Tests.* namespace: C#
// resolves the leading FF.Domain to the nearer FF.Tests.Domain and then fails to
// find the rest under it. That broke SnapCountImportServiceTests the moment this
// file was added, in a file nobody had touched.
//
// FF.Tests.Services matches where the type under test lives (FF.Domain.Services)
// and shadows nothing — there is no FF.Services project.
namespace FF.Tests.Services;

/// <summary>
/// FAN-178. The defect this class exists to prevent is silent: a join between two
/// differently-sourced team strings simply matches nothing, with no exception and
/// no log line, and the affected team disappears from whatever was being built.
/// The Rams are the live case — nflverse and the odds feed write <c>LA</c>,
/// Sleeper and every roster join write <c>LAR</c> — and against a 272-game
/// schedule a missing team looks exactly like a bye.
/// </summary>
public class NflTeamNormalizerTests
{
    [Theory]
    [InlineData("LA", "LAR")]    // nflverse / The Odds API — the one that actually bit us
    [InlineData("STL", "LAR")]   // pre-2016 relocation, still in historical game logs
    [InlineData("SD", "LAC")]
    [InlineData("OAK", "LV")]
    [InlineData("WSH", "WAS")]
    [InlineData("WFT", "WAS")]
    [InlineData("JAC", "JAX")]
    [InlineData("ARZ", "ARI")]
    [InlineData("BLT", "BAL")]
    [InlineData("CLV", "CLE")]
    [InlineData("HST", "HOU")]
    public void Normalize_MapsKnownAliases_ToSleeperForm(string input, string expected) =>
        NflTeamNormalizer.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData("LAR")]
    [InlineData("KC")]
    [InlineData("NYG")]
    [InlineData("WAS")]
    public void Normalize_LeavesCanonicalValues_Untouched(string team) =>
        NflTeamNormalizer.Normalize(team).Should().Be(team);

    [Theory]
    [InlineData("la")]
    [InlineData("La")]
    [InlineData("  LA  ")]
    public void Normalize_IsCaseAndWhitespaceInsensitive(string input) =>
        NflTeamNormalizer.Normalize(input).Should().Be("LAR");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_ReturnsEmpty_ForNothing(string? input) =>
        NflTeamNormalizer.Normalize(input).Should().BeEmpty();

    /// <summary>
    /// An unrecognised value is upper-cased and passed through rather than
    /// dropped. Discarding it would reintroduce exactly the silent-disappearance
    /// behaviour this class was written to end; callers that need to know use
    /// <see cref="NflTeamNormalizer.IsCanonical"/>.
    /// </summary>
    [Fact]
    public void Normalize_PassesThrough_UnknownValues_RatherThanDiscardingThem()
    {
        NflTeamNormalizer.Normalize("FA").Should().Be("FA");
        NflTeamNormalizer.Normalize("xyz").Should().Be("XYZ");

        NflTeamNormalizer.IsCanonical("FA").Should().BeFalse();
        NflTeamNormalizer.IsCanonical("LA").Should().BeTrue("LA normalises to a real team");
    }

    [Fact]
    public void CanonicalTeams_ContainsExactly32Teams()
    {
        NflTeamNormalizer.CanonicalTeams.Should().HaveCount(32);

        // No alias may also be a canonical value, or normalisation would be
        // ambiguous depending on which direction a caller went.
        NflTeamNormalizer.CanonicalTeams.Should().NotContain("LA");
        NflTeamNormalizer.CanonicalTeams.Should().NotContain("OAK");
        NflTeamNormalizer.CanonicalTeams.Should().NotContain("SD");
    }

    [Fact]
    public void SameTeam_MatchesAcrossSources()
    {
        // The exact comparison the schedule join makes: a Sleeper roster value
        // against an nflverse feed value.
        NflTeamNormalizer.SameTeam("LAR", "LA").Should().BeTrue();
        NflTeamNormalizer.SameTeam("LV", "OAK").Should().BeTrue();
        NflTeamNormalizer.SameTeam("was", "WSH").Should().BeTrue();

        NflTeamNormalizer.SameTeam("LAR", "LAC").Should().BeFalse(
            "the two Los Angeles teams must never collapse into each other");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("LAR", null)]
    [InlineData(null, "LAR")]
    public void SameTeam_IsFalse_WhenEitherSideIsMissing(string? a, string? b) =>
        NflTeamNormalizer.SameTeam(a, b).Should().BeFalse(
            "two unknowns are not a match — treating them as one would join every " +
            "teamless player to every other");
}

// FF.Tests/Application/PlayerNameNormalizerTests.cs
//
// 2026-09-07. Every case below is a real player the calibration harness failed to
// match, taken from the TopUnmatched list on the 2026-09-06T15:16Z run. Nine of
// the ten most valuable unmatched players were generational-suffix mismatches;
// 84 of our top 250 valuations were excluded from every metric because of it.

using FF.Application.Services;
using FluentAssertions;

namespace FF.Tests.Application;

public class PlayerNameNormalizerTests
{
    [Theory]
    // The measured casualties — FantasyPros spelling on the left, Sleeper on the right.
    [InlineData("Patrick Mahomes II", "Patrick Mahomes")]
    [InlineData("James Cook III", "James Cook")]
    [InlineData("Kenneth Walker III", "Kenneth Walker")]
    [InlineData("Omar Cooper Jr.", "Omar Cooper")]
    [InlineData("Chris Brazzell II", "Chris Brazzell")]
    [InlineData("Brian Thomas Jr.", "Brian Thomas")]
    [InlineData("Chris Godwin Jr.", "Chris Godwin")]
    [InlineData("Travis Etienne Jr.", "Travis Etienne")]
    [InlineData("Tyrone Tracy Jr.", "Tyrone Tracy")]
    public void SuffixedAndUnsuffixedSpellings_NormalizeToTheSameKey(string fp, string sleeper)
        => PlayerNameNormalizer.Normalize(fp)
            .Should().Be(PlayerNameNormalizer.Normalize(sleeper));

    [Theory]
    [InlineData("Kenneth Walker III", "kenneth walker")]
    [InlineData("Robert Griffin IV", "robert griffin")]
    [InlineData("Odell Beckham Sr", "odell beckham")]
    public void StripsEachGenerationalSuffix(string input, string expected)
        => PlayerNameNormalizer.Normalize(input).Should().Be(expected);

    [Fact]
    public void LongerSuffixesAreTestedFirst()
    {
        // "iii" must be matched before "ii", or this leaves a stray "i" behind
        // and the name still fails to match.
        PlayerNameNormalizer.Normalize("Kenneth Walker III").Should().NotEndWith(" i");
        PlayerNameNormalizer.Normalize("Kenneth Walker III").Should().Be("kenneth walker");
    }

    [Theory]
    [InlineData("Amon-Ra St. Brown", "amon ra st brown")]
    [InlineData("Ja'Marr Chase", "ja marr chase")]
    [InlineData("Jaxon Smith-Njigba", "jaxon smith njigba")]
    public void PunctuationIsStrippedAndWhitespaceCollapsed(string input, string expected)
        => PlayerNameNormalizer.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData("Deebo Samuel")]
    [InlineData("Bijan Robinson")]
    public void NamesWithoutSuffixesAreUnchangedBeyondCasing(string name)
        => PlayerNameNormalizer.Normalize(name).Should().Be(name.ToLowerInvariant());

    [Theory]
    [InlineData("Jr")]
    [InlineData("II")]
    public void ASuffixAloneIsNotStrippedToNothing(string name)
    {
        // A bad row consisting only of a suffix must not normalize to the empty
        // string — that would make every such row match every other one.
        PlayerNameNormalizer.Normalize(name).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NullOrBlankIsEmpty(string? name)
        => PlayerNameNormalizer.Normalize(name).Should().BeEmpty();

    // ── Placeholders ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Duplicate Player")]
    [InlineData("Player Invalid")]
    [InlineData("Invalid Player")]
    [InlineData("Unknown Player")]
    [InlineData("duplicate player")]
    public void SleeperPlaceholderRowsAreRecognised(string name)
        => PlayerNameNormalizer.IsPlaceholder(name).Should().BeTrue();

    [Theory]
    [InlineData("Patrick Mahomes")]
    [InlineData("Duplicate Playerson")]
    [InlineData(null)]
    public void RealPlayersAreNotFlaggedAsPlaceholders(string? name)
        => PlayerNameNormalizer.IsPlaceholder(name).Should().BeFalse();

    // ── Diacritics ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Eddy Piñeiro", "Eddy Pineiro")]
    [InlineData("José Ramírez", "Jose Ramirez")]
    [InlineData("Renê Paredes", "Rene Paredes")]
    public void AccentedAndAsciiSpellingsMatch(string accented, string ascii)
    {
        // 2026-09-07. `char.IsLetterOrDigit('ñ')` is TRUE, so accented letters
        // passed straight through the punctuation filter and never folded.
        // FFC publishes "Eddy Piñeiro"; Sleeper's player table carries the ASCII
        // spelling. He was the single remaining unmatched row in the FFC ADP sync
        // after the kicker-token fix — one kicker invisible on the draft board
        // for want of a tilde.
        PlayerNameNormalizer.Normalize(accented)
            .Should().Be(PlayerNameNormalizer.Normalize(ascii));
    }

    [Fact]
    public void DiacriticFoldingDoesNotMergeDifferentPlayers()
    {
        // Folding accents must not make distinct surnames collide.
        PlayerNameNormalizer.Normalize("Eddy Piñeiro")
            .Should().NotBe(PlayerNameNormalizer.Normalize("Eddy Pinero"));
    }

    [Fact]
    public void FoldingSurvivesTheSuffixRules()
    {
        // Accent folding runs before suffix stripping, so both still apply.
        PlayerNameNormalizer.Normalize("José Ramírez Jr.")
            .Should().Be("jose ramirez");
    }
}

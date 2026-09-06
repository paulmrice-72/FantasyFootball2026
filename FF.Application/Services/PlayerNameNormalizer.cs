// FF.Application/Services/PlayerNameNormalizer.cs
namespace FF.Application.Services;

/// <summary>
/// One normalizer for matching player names across sources (2026-09-07).
///
/// WHY THIS EXISTS
///
/// The codebase had at least two different rules for the same job.
/// <c>SyncRedraftAdpJob.NormalizeName</c> stripped generational suffixes;
/// <c>ImportFantasyProsDynastyRankingsCommandHandler.NormalizeName</c> did not.
/// The second one is what broke the calibration harness: FantasyPros writes
/// "Patrick Mahomes II" and Sleeper writes "Patrick Mahomes", so they normalized
/// to different strings and never matched.
///
/// Measured cost — of the ten most valuable players the harness could not match,
/// NINE were suffix mismatches:
///
///   Patrick Mahomes II · James Cook III · Kenneth Walker III · Omar Cooper Jr.
///   Chris Brazzell II · Brian Thomas Jr. · Chris Godwin Jr. · Travis Etienne Jr.
///   Tyrone Tracy Jr.
///
/// 84 of our top 250 valuations were excluded from every calibration metric, and
/// the model was being judged on what survived.
/// </summary>
public static class PlayerNameNormalizer
{
    /// <summary>
    /// Longest first — "iii" must be tested before "ii", or "Kenneth Walker III"
    /// normalizes to "kenneth walker i" and still fails to match.
    ///
    /// Deliberately excludes "v": a trailing single "v" is far more likely to be
    /// part of a real surname than a fifth-generation suffix, and getting that
    /// wrong silently merges two different players, which is worse than failing
    /// to match one.
    /// </summary>
    private static readonly string[] GenerationalSuffixes = ["iii", "ii", "iv", "jr", "sr"];

    /// <summary>
    /// Sleeper's player table carries placeholder rows for retired and void
    /// entries. They are not people and must not be valued, ranked, or matched.
    /// </summary>
    private static readonly HashSet<string> PlaceholderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "player invalid", "duplicate player", "invalid player", "unknown player"
    };

    /// <summary>
    /// Lowercase, strip punctuation, collapse whitespace, then remove a trailing
    /// generational suffix. Applied to BOTH sides of any name comparison — a
    /// normalizer only helps if every source goes through the same one.
    /// </summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var chars = name.ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ');

        var collapsed = string.Join(
            ' ',
            new string([.. chars]).Split(' ', StringSplitOptions.RemoveEmptyEntries));

        foreach (var suffix in GenerationalSuffixes)
        {
            if (!collapsed.EndsWith(' ' + suffix, StringComparison.Ordinal)) continue;

            var trimmed = collapsed[..^(suffix.Length + 1)].Trim();

            // Never strip away the whole name. "Jr" alone is a bad row, not a
            // suffix, and returning "" would match it against every other bad row.
            if (trimmed.Length > 0) return trimmed;
        }

        return collapsed;
    }

    /// <summary>
    /// True for Sleeper's non-player placeholder rows. Checked on the RAW name —
    /// these are exact known strings, not fuzzy matches.
    ///
    /// This existed as a private set inside SeedSeasonAverageSimsCommandHandler,
    /// so the season-average seed knew to skip them while the dynasty valuation
    /// pipeline did not. The result was a row literally named "Duplicate Player"
    /// carrying a TradeValue of 81.8 — around 20th on the dynasty board.
    /// </summary>
    public static bool IsPlaceholder(string? name) =>
        !string.IsNullOrWhiteSpace(name)
        && PlaceholderNames.Contains(name.Trim());
}

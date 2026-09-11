namespace FF.Domain.Services;

/// <summary>
/// The single source of truth for NFL team abbreviations.
///
/// <para>
/// FAN-178. Four sources feed this system and three of them disagree.
/// Sleeper (which populates <c>Player.NflTeam</c>, and is therefore what every
/// roster join uses) writes <c>LAR</c>. nflfastR game logs and the nflverse
/// schedule write <c>LA</c>. <c>TeamNameResolver</c>, which fills
/// <c>vegas_lines</c>, was written against the nflverse convention and also
/// produces <c>LA</c>. The bye-week table in <c>GetMyRosterQueryHandler</c>
/// was written against Sleeper and produces <c>LAR</c>. Nothing reconciled them,
/// so a join between any two of those sources dropped the Rams silently — no
/// exception, no log line, just a team that never matched.
/// </para>
///
/// <para>
/// Canonical form is the <b>Sleeper</b> convention, because Sleeper is the
/// roster system of record: leagues, rosters and <c>Player.NflTeam</c> all come
/// from it, so canonicalising anywhere else would mean normalising the majority
/// of joins instead of the minority.
/// </para>
///
/// <para>
/// Normalise at the boundary — the moment a value arrives from an external
/// source — not at the point of comparison. A value that enters the database
/// un-normalised will be compared un-normalised somewhere nobody is looking.
/// </para>
/// </summary>
public static class NflTeamNormalizer
{
    /// <summary>
    /// Non-canonical abbreviations seen in the sources this system reads,
    /// mapped to the Sleeper form. Anything not listed is already canonical
    /// or is not a team abbreviation at all.
    /// </summary>
    private static readonly Dictionary<string, string> Aliases =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Rams — the one that actually bit us.
            ["LA"] = "LAR",
            ["STL"] = "LAR",   // pre-2016 relocation, present in historical game logs

            // Chargers
            ["SD"] = "LAC",    // pre-2017 relocation

            // Raiders
            ["OAK"] = "LV",    // pre-2020 relocation

            // Commanders
            ["WSH"] = "WAS",   // ESPN convention
            ["WFT"] = "WAS",   // 2020-2021 interim name

            // PFR / miscellaneous source variants
            ["JAC"] = "JAX",
            ["ARZ"] = "ARI",
            ["BLT"] = "BAL",
            ["CLV"] = "CLE",
            ["HST"] = "HOU",
        };

    /// <summary>
    /// The 32 canonical abbreviations. Exposed so callers can assert that a
    /// parsed feed covers the league rather than discovering a gap downstream.
    /// </summary>
    public static readonly IReadOnlySet<string> CanonicalTeams = new HashSet<string>(
        StringComparer.OrdinalIgnoreCase)
    {
        "ARI", "ATL", "BAL", "BUF", "CAR", "CHI", "CIN", "CLE",
        "DAL", "DEN", "DET", "GB",  "HOU", "IND", "JAX", "KC",
        "LAC", "LAR", "LV",  "MIA", "MIN", "NE",  "NO",  "NYG",
        "NYJ", "PHI", "PIT", "SEA", "SF",  "TB",  "TEN", "WAS",
    };

    /// <summary>
    /// Returns the canonical (Sleeper) abbreviation for <paramref name="team"/>.
    /// Null, empty and whitespace pass through as <see cref="string.Empty"/>.
    /// An unrecognised value is returned upper-cased and unchanged rather than
    /// discarded — losing it silently is the behaviour this class exists to end.
    /// Use <see cref="IsCanonical"/> when the caller needs to know.
    /// </summary>
    public static string Normalize(string? team)
    {
        if (string.IsNullOrWhiteSpace(team)) return string.Empty;

        var trimmed = team.Trim();

        return Aliases.TryGetValue(trimmed, out var canonical)
            ? canonical
            : trimmed.ToUpperInvariant();
    }

    /// <summary>
    /// True when the normalised value is one of the 32 current teams.
    /// False for free agents, retired teams, defence placeholders and typos.
    /// </summary>
    public static bool IsCanonical(string? team) =>
        CanonicalTeams.Contains(Normalize(team));

    /// <summary>
    /// True when both values refer to the same team once normalised.
    /// Prefer this over <c>==</c> anywhere two differently-sourced team strings meet.
    /// </summary>
    public static bool SameTeam(string? a, string? b)
    {
        var left = Normalize(a);
        return left.Length > 0
            && left.Equals(Normalize(b), StringComparison.OrdinalIgnoreCase);
    }
}

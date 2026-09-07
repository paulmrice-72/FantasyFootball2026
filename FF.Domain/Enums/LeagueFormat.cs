// FF.Domain/Enums/LeagueFormat.cs
namespace FF.Domain.Enums;

/// <summary>
/// What kind of league this is. Three values, because Sleeper has three.
///
/// 2026-09-07. `League.LeagueType` is a string written by
/// SleeperLeagueImportService.MapLeagueType, which maps Sleeper's
/// `settings.type`: 2 → "Dynasty", 1 → "Keeper", anything else → "Redraft".
/// So "Keeper" has been a real, reachable value since league import was built.
///
/// A repo-wide search for the literal "Keeper" returned exactly one hit: the
/// line that writes it. Nothing read it. The only two consumers each assumed a
/// two-valued world, and picked opposite defaults:
///
///     RookieDraftBoard.razor:  _isRedraftMode   = LeagueType != "Dynasty";
///     MyTeam.razor:            _isDynastyLeague = LeagueType != "Redraft";
///
/// A keeper league is therefore redraft on one page and dynasty on the other,
/// simultaneously, which is exactly what Paul saw: a redraft draft board and a
/// dynasty draft-picks panel for the same team on the same afternoon.
///
/// This enum exists so that no caller has to guess again. Parse once, branch on
/// a named capability, never on a string comparison.
/// </summary>
public enum LeagueFormat
{
    /// <summary>Fresh draft every season; no roster carries over.</summary>
    Redraft = 0,

    /// <summary>
    /// A subset of last season's roster carries over as keepers; the rest is
    /// redrafted. Similar to dynasty, and not the same as it.
    /// </summary>
    Keeper = 1,

    /// <summary>The whole roster carries over; drafts are rookie-only.</summary>
    Dynasty = 2
}

public static class LeagueFormatExtensions
{
    /// <summary>
    /// Reads the string stored on <c>League.LeagueType</c>. Unknown and null
    /// values fall to Redraft, which is the safest default: it is the format
    /// that assumes the least about a roster.
    /// </summary>
    public static LeagueFormat ParseLeagueFormat(string? storedLeagueType) =>
        storedLeagueType?.Trim().ToUpperInvariant() switch
        {
            "DYNASTY" => LeagueFormat.Dynasty,
            "KEEPER" => LeagueFormat.Keeper,
            _ => LeagueFormat.Redraft
        };

    /// <summary>
    /// Sleeper's <c>settings.type</c>. Single source of this mapping — importers
    /// should call this rather than keeping their own switch.
    /// </summary>
    public static LeagueFormat FromSleeperType(int sleeperLeagueType) =>
        sleeperLeagueType switch
        {
            2 => LeagueFormat.Dynasty,
            1 => LeagueFormat.Keeper,
            _ => LeagueFormat.Redraft
        };

    /// <summary>The string persisted on League.LeagueType. Round-trips with ParseLeagueFormat.</summary>
    public static string ToStorageString(this LeagueFormat format) => format.ToString();

    // ── Capabilities ─────────────────────────────────────────────────────────
    //
    // Named questions rather than equality checks. A caller that asks "is this
    // dynasty" gets the wrong answer for keeper half the time; a caller that
    // asks "does this draft from the rookie pool" cannot.

    /// <summary>
    /// True when the draft board should show the rookie pool rather than the
    /// full redraft board. Only dynasty leagues draft rookies exclusively — a
    /// keeper league drafts the whole player pool, minus keepers.
    /// </summary>
    public static bool UsesRookieDraftPool(this LeagueFormat format) =>
        format == LeagueFormat.Dynasty;

    /// <summary>
    /// True when future draft picks are ownable, tradeable assets worth showing
    /// on a team page. Keeper leagues commonly trade picks; redraft leagues do
    /// not have them at all.
    /// </summary>
    public static bool HasTradeablePicks(this LeagueFormat format) =>
        format is LeagueFormat.Dynasty or LeagueFormat.Keeper;

    /// <summary>
    /// True when the roster Sleeper reports for your team during a draft should
    /// count toward roster needs.
    ///
    /// 2026-09-07, and this is the one that cost Paul a kicker.
    ///
    /// Sleeper returns your live roster for the league. In a DYNASTY league that
    /// roster is your actual team and a rookie draft adds to it, so it counts.
    ///
    /// In a KEEPER league, before rollover, that roster is still last season's
    /// entire team — not your keepers, because Sleeper does not distinguish them
    /// until the season rolls. Paul's carried a kicker (Evan McPherson) and a
    /// defense (the Rams). The draft board counted them, decided the K and DEF
    /// slots were filled, returned zero urgency for both, and told him every
    /// starting slot was covered while he sat one pick from the end of a draft
    /// with no kicker.
    ///
    /// So: only dynasty. A keeper league's needs come from the draft itself
    /// until keeper designation exists, at which point keepers can be added
    /// back deliberately rather than by accident.
    /// </summary>
    public static bool CarriedRosterCountsTowardDraftNeeds(this LeagueFormat format) =>
        format == LeagueFormat.Dynasty;
}

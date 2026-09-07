// FF.Domain/ValueObjects/UserContext.cs
namespace FF.Domain.ValueObjects;

public record UserContext(
    string UserId,
    string? SleeperUserId,
    string? SleeperUsername,
    bool IsSleeperLinked,
    IReadOnlyList<LeagueContext> Leagues,
    string? ActiveLeagueId
);

public record LeagueContext(
    string LeagueId,
    string LeagueName,
    int Season,
    string Role,
    bool IsActive,
    string LeagueType = "Redraft",
    string? Avatar = null,
    int TotalTeams = 12,           // ← from League.TotalRosters

    /// <summary>
    /// FAN-100 (2026-09-07): the league's Sleeper roster_positions string, e.g.
    /// "QB,RB,RB,WR,WR,TE,WRRB_FLEX,WRRB_FLEX,K,DEF,BN,BN,BN,BN,BN,BN".
    ///
    /// This has been sitting in Postgres since 2026-04-12 (League.RosterPositions,
    /// populated on every import/sync) and was never carried to any client, which
    /// is why the draft board asked the user to retype his own starting lineup and
    /// had no way at all to express the K and DEF his league requires.
    ///
    /// Null when Sleeper has not reported positions for the league yet — callers
    /// must treat null as "unknown", not as "no slots".
    /// </summary>
    string? RosterPositions = null
);

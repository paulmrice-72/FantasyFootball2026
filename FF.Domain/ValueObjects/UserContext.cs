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
    int TotalTeams = 12            // ← NEW: from League.TotalRosters
);
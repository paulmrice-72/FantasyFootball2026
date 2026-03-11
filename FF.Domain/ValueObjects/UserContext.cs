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
    bool IsActive
);
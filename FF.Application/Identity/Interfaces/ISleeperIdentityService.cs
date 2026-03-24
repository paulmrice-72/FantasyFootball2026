namespace FF.Application.Identity.Interfaces;

public record SleeperUserInfo(
    string SleeperUserId,
    string Username,
    string? DisplayName,
    string? Avatar
);

public record SleeperUserLeague(
    string LeagueId,
    string Name,
    int Season,
    string Status,
    int TotalRosters,
    string LeagueType   // "Redraft", "Keeper", "Dynasty"
);

public interface ISleeperIdentityService
{
    Task<SleeperUserInfo?> GetUserByUsernameAsync(
        string username, CancellationToken cancellationToken = default);

    Task<bool> VerifyLeagueMembershipAsync(
        string sleeperUserId, string leagueId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SleeperUserLeague>> GetUserLeaguesAsync(
        string sleeperUserId, int season,
        CancellationToken cancellationToken = default);
}
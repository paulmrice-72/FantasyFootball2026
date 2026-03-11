namespace FF.Application.Identity.Interfaces;

public record SleeperUserInfo(
    string SleeperUserId,
    string Username,
    string? DisplayName,
    string? Avatar
);

public interface ISleeperIdentityService
{
    Task<SleeperUserInfo?> GetUserByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<bool> VerifyLeagueMembershipAsync(string sleeperUserId, string leagueId, CancellationToken cancellationToken = default);
}
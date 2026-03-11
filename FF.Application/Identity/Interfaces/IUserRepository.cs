namespace FF.Application.Identity.Interfaces;

public record AppUserDto(
    string Id,
    string? SleeperUserId,
    string? SleeperUsername,
    string? Email
);

public interface IUserRepository
{
    Task<AppUserDto?> GetByIdAsync(string userId, CancellationToken cancellationToken = default);
    Task<AppUserDto?> GetBySleeperUserIdAsync(string sleeperUserId, CancellationToken cancellationToken = default);
    Task LinkSleeperAccountAsync(string userId, string sleeperUserId, string sleeperUsername, CancellationToken cancellationToken = default);
}
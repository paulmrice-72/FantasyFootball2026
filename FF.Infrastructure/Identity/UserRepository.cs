using FF.Application.Identity.Interfaces;
using FF.Infrastructure.Persistence.SQL;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FF.Infrastructure.Identity;

public class UserRepository(
    UserManager<ApplicationUser> userManager
) : IUserRepository
{
    public async Task<AppUserDto?> GetByIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return user is null ? null : ToDto(user);
    }

    public async Task<AppUserDto?> GetBySleeperUserIdAsync(string sleeperUserId, CancellationToken cancellationToken = default)
    {
        var user = await userManager.Users
            .FirstOrDefaultAsync(u => u.SleeperUserId == sleeperUserId, cancellationToken);

        return user is null ? null : ToDto(user);
    }

    public async Task LinkSleeperAccountAsync(string userId, string sleeperUserId, string sleeperUsername, CancellationToken cancellationToken = default)
    {
        var user = await userManager.Users
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} not found.");

        user.SleeperUserId = sleeperUserId;
        user.SleeperUsername = sleeperUsername;
        user.SleeperLinkedAt = DateTime.UtcNow;

        await userManager.UpdateAsync(user);
    }

    private static AppUserDto ToDto(ApplicationUser user) => new(
        user.Id,
        user.SleeperUserId,
        user.SleeperUsername,
        user.Email
    );
}
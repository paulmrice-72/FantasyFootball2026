using FF.Application.Identity.Interfaces;
using MediatR;

namespace FF.Application.Identity.Commands.LinkSleeperAccount;

public class LinkSleeperAccountCommandHandler(
    ISleeperIdentityService sleeperIdentityService,
    IUserRepository userRepository
) : IRequestHandler<LinkSleeperAccountCommand, LinkSleeperAccountResult>
{
    public async Task<LinkSleeperAccountResult> Handle(
        LinkSleeperAccountCommand request,
        CancellationToken cancellationToken)
    {
        // 1. Look up Sleeper user by username
        var sleeperUser = await sleeperIdentityService.GetUserByUsernameAsync(
            request.SleeperUsername, cancellationToken);

        if (sleeperUser is null)
            return new LinkSleeperAccountResult(false, null,
                $"Sleeper user '{request.SleeperUsername}' not found.");

        // 2. Check Sleeper account isn't already linked to a different user
        var existingUser = await userRepository.GetBySleeperUserIdAsync(
            sleeperUser.SleeperUserId, cancellationToken);

        if (existingUser is not null && existingUser.Id != request.UserId)
            return new LinkSleeperAccountResult(false, null,
                "This Sleeper account is already linked to another user.");

        // 3. Link the account
        var appUser = await userRepository.GetByIdAsync(request.UserId, cancellationToken);
        if (appUser is null)
            return new LinkSleeperAccountResult(false, null, "User not found.");

        await userRepository.LinkSleeperAccountAsync(
            request.UserId,
            sleeperUser.SleeperUserId,
            sleeperUser.Username,
            cancellationToken);

        return new LinkSleeperAccountResult(true, sleeperUser.SleeperUserId, null);
    }
}
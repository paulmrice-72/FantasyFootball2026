using FF.Application.Identity.Interfaces;
using MediatR;

namespace FF.Application.Identity.Commands.LinkSleeperAccount;

public class LinkSleeperAccountCommandHandler(
    ISleeperIdentityService sleeperIdentityService,
    IUserRepository userRepository,
    ILeagueMembershipRepository leagueMembershipRepository
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

        // 4. Fetch user's leagues from Sleeper and create memberships
        // Fetch both current and previous season to catch active leagues
        var currentSeason = DateTime.UtcNow.Month >= 3
            ? DateTime.UtcNow.Year
            : DateTime.UtcNow.Year - 1;

        var previousSeason = currentSeason - 1;
        var seasons = new[] { currentSeason, previousSeason };

        foreach (var season in seasons)
        {
            var leagues = await sleeperIdentityService.GetUserLeaguesAsync(
                sleeperUser.SleeperUserId, season, cancellationToken);

            foreach (var league in leagues)
            {
                await leagueMembershipRepository.AddMembershipAsync(
                    userId: request.UserId,
                    sleeperUserId: sleeperUser.SleeperUserId,
                    leagueId: league.LeagueId,
                    leagueName: league.Name,
                    season: league.Season,
                    role: "member",
                    cancellationToken: cancellationToken);
            }
        }

        return new LinkSleeperAccountResult(true, sleeperUser.SleeperUserId, null);
    }
}
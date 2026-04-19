using FF.Application.Features.DraftTools.Commands.ImportConsensusAdp;
using FF.Application.Identity.Interfaces;
using FF.Application.Interfaces.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Identity.Commands.LinkSleeperAccount;

// Inject ISleeperLeagueImportService into the constructor
public class LinkSleeperAccountCommandHandler(
    ISleeperIdentityService sleeperIdentityService,
    IUserRepository userRepository,
    ILeagueMembershipRepository leagueMembershipRepository,
    ISleeperLeagueImportService leagueImportService, 
    ILogger<LinkSleeperAccountCommandHandler> logger   // ← add
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
                    leagueType: league.LeagueType,
                    cancellationToken: cancellationToken);

                // ← Add: ensure Leagues row exists for every membership
                try
                {
                    await leagueImportService.ImportLeagueAsync(
                        league.LeagueId, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Log and continue — don't fail the whole link if one import errors
                    logger.LogError(ex,
                        "Failed to import league {LeagueId} during Sleeper link", league.LeagueId);
                }
            }
        }

        return new LinkSleeperAccountResult(true, sleeperUser.SleeperUserId, null);
    }
}
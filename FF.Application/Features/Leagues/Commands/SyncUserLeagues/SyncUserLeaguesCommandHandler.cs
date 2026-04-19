using FF.Application.Identity.Interfaces;
using FF.Application.Interfaces.Services;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Leagues.Commands.SyncUserLeagues;

public class SyncUserLeaguesCommandHandler(
    ILeagueMembershipRepository leagueMembershipRepository,
    ISleeperLeagueImportService leagueImportService,
    ILogger<SyncUserLeaguesCommandHandler> logger)
    : IRequestHandler<SyncUserLeaguesCommand, Result<SyncUserLeaguesResult>>
{
    public async Task<Result<SyncUserLeaguesResult>> Handle(
        SyncUserLeaguesCommand request,
        CancellationToken cancellationToken)
    {
        var memberships = await leagueMembershipRepository
            .GetLeaguesForUserAsync(request.UserId, cancellationToken);

        if (memberships.Count == 0)
        {
            logger.LogWarning(
                "SyncUserLeagues — no memberships found for user {UserId}",
                request.UserId);
            return Result.Success(new SyncUserLeaguesResult(0, 0));
        }

        var synced = 0;
        var failed = 0;

        foreach (var membership in memberships)
        {
            try
            {
                await leagueImportService.SyncLeagueAsync(
                    membership.LeagueId, cancellationToken);
                synced++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to sync league {LeagueId} for user {UserId}",
                    membership.LeagueId, request.UserId);
                failed++;
            }
        }

        logger.LogInformation(
            "SyncUserLeagues complete for user {UserId} — {Synced} synced, {Failed} failed",
            request.UserId, synced, failed);

        return Result.Success(new SyncUserLeaguesResult(synced, failed));
    }
}
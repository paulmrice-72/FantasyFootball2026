// FF.Application/DraftTools/Commands/StartDraftSession/StartDraftSessionCommandHandler.cs
using FF.Application.Common.Models;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.DraftTools.Commands.StartDraftSession;

public class StartDraftSessionCommandHandler(
    IDraftSessionRepository sessionRepository,
    ISleeperDraftService sleeperDraftService,
    ILogger<StartDraftSessionCommandHandler> logger)
    : IRequestHandler<StartDraftSessionCommand, Result<string>>
{
    public async Task<Result<string>> Handle(
        StartDraftSessionCommand request, CancellationToken cancellationToken)
    {
        // Close any existing active session for this user+league
        var existing = await sessionRepository.GetActiveByUserAndLeagueAsync(
            request.UserId, request.LeagueId, cancellationToken);

        if (existing is not null)
        {
            existing.IsActive = false;
            await sessionRepository.UpdateAsync(existing, cancellationToken);
            logger.LogInformation(
                "Closed existing draft session {Id} for league {League}",
                existing.Id, request.LeagueId);
        }

        // Look up the active Sleeper draft_id and the user's roster_id
        string? sleeperDraftId = null;
        int? myRosterId = null;

        try
        {
            sleeperDraftId = await sleeperDraftService.GetActiveDraftIdAsync(
                request.LeagueId, cancellationToken);

            if (sleeperDraftId is null)
                logger.LogInformation(
                    "No active draft found for league {League} — manual pick mode only",
                    request.LeagueId);

            if (!string.IsNullOrEmpty(request.SleeperUserId))
            {
                myRosterId = await sleeperDraftService.GetMyRosterIdAsync(
                    request.LeagueId, request.SleeperUserId, cancellationToken);

                if (myRosterId.HasValue)
                    logger.LogInformation(
                        "User {UserId} mapped to roster_id {RosterId} in league {League}",
                        request.SleeperUserId, myRosterId, request.LeagueId);
            }
        }
        catch (Exception ex)
        {
            // Non-fatal — session still starts, just without auto-sync
            logger.LogWarning(ex,
                "Could not look up Sleeper draft for league {League} — session will be manual",
                request.LeagueId);
        }

        var session = new DraftSessionDocument
        {
            UserId = request.UserId,
            LeagueId = request.LeagueId,
            LeagueName = request.LeagueName,
            Season = request.Season,
            IsActive = true,
            SleeperDraftId = sleeperDraftId,
            MyRosterId = myRosterId,
            Picks = [],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await sessionRepository.InsertAsync(session, cancellationToken);

        logger.LogInformation(
            "Started draft session {Id} for user {UserId} league {League} (draftId: {DraftId})",
            session.Id, request.UserId, request.LeagueId, sleeperDraftId ?? "none");

        return Result<string>.Success(session.Id);
    }
}
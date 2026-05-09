// FF.Application/Features/DraftTools/Queries/SyncSleeperPicks/SyncSleeperPicksQueryHandler.cs
using FF.Application.Features.DraftTools.Commands.RecordDraftPick;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.DraftTools.Queries.SyncSleeperPicks;

public class SyncSleeperPicksQueryHandler(
    IDraftSessionRepository sessionRepository,
    ISleeperDraftService sleeperDraftService,
    IMediator mediator,
    ILogger<SyncSleeperPicksQueryHandler> logger)
    : IRequestHandler<SyncSleeperPicksQuery, Result<SyncSleeperPicksResult>>
{
    public async Task<Result<SyncSleeperPicksResult>> Handle(
        SyncSleeperPicksQuery request, CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetByIdAsync(request.SessionId, cancellationToken);

        if (session is null)
            return Result.Failure<SyncSleeperPicksResult>(
                Error.NotFound("Draft.SessionNotFound", "Draft session not found."));

        if (session.UserId != request.UserId)
            return Result.Failure<SyncSleeperPicksResult>(
                Error.Unauthorized("Draft.NotOwner"));

        if (!session.IsActive)
            return Result.Failure<SyncSleeperPicksResult>(
                Error.Validation("Draft.SessionClosed", "This draft session is no longer active."));

        // No draft linked — nothing to sync (manual mode)
        if (string.IsNullOrEmpty(session.SleeperDraftId))
        {
            return Result<SyncSleeperPicksResult>.Success(new SyncSleeperPicksResult(
                NewPicks: [],
                TotalPicksInSession: session.Picks.Count,
                DraftComplete: false));
        }

        List<SleeperMadePickDto> sleeperPicks;
        try
        {
            sleeperPicks = await sleeperDraftService.GetMadePicksAsync(
                session.SleeperDraftId, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Failed to fetch Sleeper picks for draft {DraftId}", session.SleeperDraftId);
            // Return empty — transient failure, client will retry in 30s
            return Result<SyncSleeperPicksResult>.Success(new SyncSleeperPicksResult(
                NewPicks: [],
                TotalPicksInSession: session.Picks.Count,
                DraftComplete: false));
        }

        // Build set of already-recorded SleeperPlayerIds for fast diff
        var recordedIds = session.Picks.Select(p => p.SleeperPlayerId).ToHashSet();

        // Draft is complete when every pick has a player_id — compare total slots
        // We can't know total slots here easily, so treat "no new picks AND > 0 picks exist" as possible complete;
        // the DraftComplete flag is best-effort — the timer will just stop returning new picks naturally
        bool draftComplete = sleeperPicks.Count > 0 && sleeperPicks.All(p => recordedIds.Contains(p.PlayerId));

        var newPicks = new List<SyncedPickDto>();

        foreach (var pick in sleeperPicks)
        {
            if (recordedIds.Contains(pick.PlayerId)) continue;

            // Determine if this is the user's pick
            bool isMyPick = session.MyRosterId.HasValue
                && pick.RosterId == session.MyRosterId.Value.ToString();

            // Record via existing command (idempotent)
            var recordResult = await mediator.Send(
                new RecordDraftPickCommand(
                    SessionId: request.SessionId,
                    UserId: request.UserId,
                    SleeperPlayerId: pick.PlayerId,
                    PlayerName: pick.PlayerName,
                    Position: pick.Position,
                    Round: pick.Round,
                    Slot: pick.DraftSlot,
                    PickedByTeamName: null,
                    IsMyPick: isMyPick),
                cancellationToken);

            if (recordResult.IsSuccess)
            {
                newPicks.Add(new SyncedPickDto(
                    SleeperPlayerId: pick.PlayerId,
                    PlayerName: pick.PlayerName,
                    Position: pick.Position,
                    Round: pick.Round,
                    Slot: pick.DraftSlot,
                    IsMyPick: isMyPick));

                logger.LogInformation(
                    "Auto-synced pick: {Player} R{Round} S{Slot} isMyPick={IsMyPick} in session {Id}",
                    pick.PlayerName, pick.Round, pick.DraftSlot, isMyPick, request.SessionId);
            }
        }

        // Reload to get accurate total
        var updated = await sessionRepository.GetByIdAsync(request.SessionId, cancellationToken);

        return Result<SyncSleeperPicksResult>.Success(new SyncSleeperPicksResult(
            NewPicks: newPicks,
            TotalPicksInSession: updated?.Picks.Count ?? session.Picks.Count + newPicks.Count,
            DraftComplete: draftComplete));
    }
}
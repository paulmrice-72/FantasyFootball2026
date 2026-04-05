// FF.Application/DraftTools/Commands/RecordDraftPick/RecordDraftPickCommandHandler.cs
using FF.Application.Common.Models;
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.DraftTools.Commands.RecordDraftPick;

public class RecordDraftPickCommandHandler(
    IDraftSessionRepository sessionRepository,
    ILogger<RecordDraftPickCommandHandler> logger)
    : IRequestHandler<RecordDraftPickCommand, Result>
{
    public async Task<Result> Handle(
        RecordDraftPickCommand request, CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetByIdAsync(
            request.SessionId, cancellationToken);

        if (session is null)
            return Result.Failure(
                Error.NotFound("Draft.SessionNotFound", "Draft session not found."));

        if (session.UserId != request.UserId)
            return Result.Failure(
                Error.Unauthorized("Draft.NotOwner"));

        if (!session.IsActive)
            return Result.Failure(
                Error.Validation("Draft.SessionClosed", "This draft session is no longer active."));

        // Idempotent — ignore duplicate picks
        if (session.Picks.Any(p => p.SleeperPlayerId == request.SleeperPlayerId))
        {
            logger.LogWarning(
                "Duplicate pick attempt: {Player} already in session {Id}",
                request.PlayerName, request.SessionId);
            return Result.Success();
        }

        session.Picks.Add(new DraftPick
        {
            SleeperPlayerId = request.SleeperPlayerId,
            PlayerName = request.PlayerName,
            Position = request.Position,
            Round = request.Round,
            Slot = request.Slot,
            PickedByTeamName = request.PickedByTeamName,
            IsMyPick = request.IsMyPick,
            PickedAt = DateTime.UtcNow
        });

        await sessionRepository.UpdateAsync(session, cancellationToken);

        logger.LogInformation(
            "Pick recorded: {Player} R{Round}S{Slot} in session {Id}",
            request.PlayerName, request.Round, request.Slot, request.SessionId);

        return Result.Success();
    }
}
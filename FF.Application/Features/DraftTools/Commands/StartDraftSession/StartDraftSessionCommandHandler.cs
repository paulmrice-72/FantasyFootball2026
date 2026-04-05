// FF.Application/DraftTools/Commands/StartDraftSession/StartDraftSessionCommandHandler.cs
using FF.Application.Common.Models;
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.DraftTools.Commands.StartDraftSession;

public class StartDraftSessionCommandHandler(
    IDraftSessionRepository sessionRepository,
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

        var session = new DraftSessionDocument
        {
            UserId = request.UserId,
            LeagueId = request.LeagueId,
            LeagueName = request.LeagueName,
            Season = request.Season,
            IsActive = true,
            Picks = [],
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await sessionRepository.InsertAsync(session, cancellationToken);

        logger.LogInformation(
            "Started draft session {Id} for user {UserId} league {League}",
            session.Id, request.UserId, request.LeagueId);

        return Result<string>.Success(session.Id);
    }
}
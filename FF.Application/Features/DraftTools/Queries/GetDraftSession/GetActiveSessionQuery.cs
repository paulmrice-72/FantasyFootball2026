// FF.Application/Features/DraftTools/Queries/GetActiveSession/GetActiveSessionQuery.cs
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.DraftTools.Queries.GetDraftSession;

public record GetActiveSessionQuery(
    string UserId,
    string LeagueId) : IRequest<Result<DraftSessionDocument>>;

public class GetActiveSessionQueryHandler(IDraftSessionRepository sessionRepository)
    : IRequestHandler<GetActiveSessionQuery, Result<DraftSessionDocument>>
{
    public async Task<Result<DraftSessionDocument>> Handle(
        GetActiveSessionQuery request, CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetActiveByUserAndLeagueAsync(
            request.UserId, request.LeagueId, cancellationToken);

        return session is not null
            ? Result.Success<DraftSessionDocument>(session)
            : Result.Failure<DraftSessionDocument>(
                Error.NotFound("Draft.NoActiveSession", "No active draft session found."));
    }
}
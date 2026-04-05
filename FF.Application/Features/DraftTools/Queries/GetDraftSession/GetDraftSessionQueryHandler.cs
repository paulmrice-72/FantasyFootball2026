// FF.Application/DraftTools/Queries/GetDraftSession/GetDraftSessionQueryHandler.cs
using FF.Application.Common.Models;
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.DraftTools.Queries.GetDraftSession;

public class GetDraftSessionQueryHandler(IDraftSessionRepository sessionRepository)
    : IRequestHandler<GetDraftSessionQuery, Result<DraftSessionDocument>>
{
    public async Task<Result<DraftSessionDocument>> Handle(
        GetDraftSessionQuery request, CancellationToken cancellationToken)
    {
        var session = await sessionRepository.GetByIdAsync(
            request.SessionId, cancellationToken);

        if (session is null)
            return (Result<DraftSessionDocument>)Result<DraftSessionDocument>.Failure(
                Error.NotFound("Draft.SessionNotFound", "Draft session not found."));

        if (session.UserId != request.UserId)
            return (Result<DraftSessionDocument>)Result<DraftSessionDocument>.Failure(
                Error.Unauthorized("Draft.NotOwner"));

        return Result<DraftSessionDocument>.Success(session);
    }
}
// FF.Application/DraftTools/Queries/GetDraftSession/GetDraftSessionQuery.cs
using FF.Application.Common.Models;
using FF.Domain.Documents;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.DraftTools.Queries.GetDraftSession;

public record GetDraftSessionQuery(
    string SessionId,
    string UserId) : IRequest<Result<DraftSessionDocument>>;
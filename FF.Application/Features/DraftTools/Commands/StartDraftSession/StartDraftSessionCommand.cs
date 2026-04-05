// FF.Application/DraftTools/Commands/StartDraftSession/StartDraftSessionCommand.cs
using FF.Application.Common.Models;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.DraftTools.Commands.StartDraftSession;

public record StartDraftSessionCommand(
    string UserId,
    string LeagueId,
    string LeagueName,
    int Season) : IRequest<Result<string>>; // returns session Id
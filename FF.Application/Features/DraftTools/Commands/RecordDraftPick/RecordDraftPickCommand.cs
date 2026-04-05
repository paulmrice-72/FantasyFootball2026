using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.DraftTools.Commands.RecordDraftPick;

public record RecordDraftPickCommand(
    string SessionId,
    string UserId,          // for ownership validation
    string SleeperPlayerId,
    string PlayerName,
    string Position,
    int Round,
    int Slot,
    string? PickedByTeamName,
    bool IsMyPick) : IRequest<Result>;
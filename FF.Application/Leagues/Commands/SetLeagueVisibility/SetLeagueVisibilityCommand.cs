using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Leagues.Commands.SetLeagueVisibility;

public record SetLeagueVisibilityCommand(
    string UserId,
    Guid LeagueId,
    bool IsHidden) : IRequest<Result>;
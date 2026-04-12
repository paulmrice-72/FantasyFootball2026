using MediatR;
using FF.SharedKernel.Common;

namespace FF.Application.Features.Admin.Commands.SetLeagueActiveStatus;

public record SetLeagueActiveStatusCommand(Guid LeagueId, bool IsActive) : IRequest<Result>;
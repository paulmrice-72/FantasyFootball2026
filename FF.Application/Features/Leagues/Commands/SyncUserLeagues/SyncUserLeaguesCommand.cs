using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.Leagues.Commands.SyncUserLeagues;

public record SyncUserLeaguesCommand(string UserId)
    : IRequest<Result<SyncUserLeaguesResult>>;

public record SyncUserLeaguesResult(
    int LeaguesSynced,
    int LeaguesFailed);
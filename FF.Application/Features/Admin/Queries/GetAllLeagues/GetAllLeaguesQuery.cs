using MediatR;
using FF.SharedKernel.Common;

namespace FF.Application.Features.Admin.Queries.GetAllLeagues;

public record GetAllLeaguesQuery : IRequest<Result<IReadOnlyList<AdminLeagueDto>>>;

public record AdminLeagueDto(
    Guid Id,
    string Name,
    string SleeperLeagueId,
    int Season,
    string LeagueType,
    int TotalTeams,
    bool IsActive,
    bool IsHidden);
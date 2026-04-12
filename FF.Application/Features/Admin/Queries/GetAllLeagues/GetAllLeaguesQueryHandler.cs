using FF.Application.Interfaces.Persistence;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.Admin.Queries.GetAllLeagues;

public class GetAllLeaguesQueryHandler(
    ILeagueRepository leagueRepository)
    : IRequestHandler<GetAllLeaguesQuery, Result<IReadOnlyList<AdminLeagueDto>>>
{
    public async Task<Result<IReadOnlyList<AdminLeagueDto>>> Handle(
        GetAllLeaguesQuery request,
        CancellationToken cancellationToken)
    {
        var leagues = await leagueRepository.GetAllLeaguesAsync(cancellationToken);

        var dtos = leagues
            .Select(l => new AdminLeagueDto(
                l.Id,
                l.Name,
                l.SleeperLeagueId,
                l.Season,
                l.LeagueType,
                l.TotalTeams,
                l.IsActive,
                false))
            .ToList();

        return Result.Success<IReadOnlyList<AdminLeagueDto>>(dtos);
    }
}
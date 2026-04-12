// FF.Application/Leagues/Queries/GetAllLeagues/GetAllLeaguesQuery.cs

using FF.Application.Features.Admin.Queries.GetAllLeagues;
using FF.Application.Interfaces.Persistence;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Leagues.Queries.GetAllLeagues;

// ── Query ─────────────────────────────────────────────────────────────────────

public record GetAllLeaguesQuery(string? UserId = null)
    : IRequest<Result<IReadOnlyList<AdminLeagueDto>>>;

public record LeagueSummaryDto(
    Guid Id,
    string Name,
    string SleeperLeagueId,
    int Season,
    int TotalTeams,
    bool IsActive,
    string LeagueType
);

// ── Handler ───────────────────────────────────────────────────────────────────

public class GetAllLeaguesQueryHandler(
    ILeagueRepository leagueRepository,
    IUserLeaguePreferenceRepository preferenceRepository)
    : IRequestHandler<GetAllLeaguesQuery, Result<IReadOnlyList<AdminLeagueDto>>>
{
    public async Task<Result<IReadOnlyList<AdminLeagueDto>>> Handle(
        GetAllLeaguesQuery request,
        CancellationToken cancellationToken)
    {
        var leagues = await leagueRepository.GetAllLeaguesAsync(cancellationToken);

        HashSet<Guid> hiddenIds = [];
        if (request.UserId is not null)
        {
            var prefs = await preferenceRepository.GetByUserIdAsync(
                request.UserId, cancellationToken);
            hiddenIds = prefs.Where(p => p.IsHidden).Select(p => p.LeagueId).ToHashSet();
        }

        var dtos = leagues
            .Select(l => new AdminLeagueDto(
                l.Id,
                l.Name,
                l.SleeperLeagueId,
                l.Season,
                l.LeagueType,
                l.TotalTeams,
                l.IsActive,
                hiddenIds.Contains(l.Id)))
            .ToList();

        return Result.Success<IReadOnlyList<AdminLeagueDto>>(dtos);
    }
}

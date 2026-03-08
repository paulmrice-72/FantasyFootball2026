// FF.Application/Leagues/Queries/GetAllLeagues/GetAllLeaguesQuery.cs

using FF.Application.Interfaces.Persistence;
using MediatR;

namespace FF.Application.Leagues.Queries.GetAllLeagues;

// ── Query ─────────────────────────────────────────────────────────────────────

public record GetAllLeaguesQuery : IRequest<List<LeagueSummaryDto>>;

public record LeagueSummaryDto(
    Guid Id,
    string Name,
    string SleeperLeagueId,
    int Season,
    int TotalTeams,
    bool IsActive
);

// ── Handler ───────────────────────────────────────────────────────────────────

public class GetAllLeaguesQueryHandler(IUnitOfWork unitOfWork) : IRequestHandler<GetAllLeaguesQuery, List<LeagueSummaryDto>>
{
    private readonly IUnitOfWork _unitOfWork = unitOfWork;

    public async Task<List<LeagueSummaryDto>> Handle(
        GetAllLeaguesQuery request,
        CancellationToken cancellationToken)
    {
        var leagues = await _unitOfWork.Leagues.GetAllAsync(cancellationToken);

        return [.. leagues.Select(l => new LeagueSummaryDto(
            Id: l.Id,
            Name: l.Name,
            SleeperLeagueId: l.SleeperLeagueId,
            Season: l.Season,
            TotalTeams: l.TotalTeams,
            IsActive: l.IsActive
        ))];
    }
}

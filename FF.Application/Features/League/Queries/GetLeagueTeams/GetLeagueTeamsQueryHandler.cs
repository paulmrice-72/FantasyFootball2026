using FF.Application.Interfaces.Persistence;
using MediatR;

namespace FF.Application.Features.League.Queries.GetLeagueTeams;

public class GetLeagueTeamsQueryHandler(IRosterPlayerRepository rosterPlayerRepository)
    : IRequestHandler<GetLeagueTeamsQuery, List<LeagueTeamSummaryDto>>
{
    public async Task<List<LeagueTeamSummaryDto>> Handle(
        GetLeagueTeamsQuery request, CancellationToken cancellationToken)
    {
        var rosters = await rosterPlayerRepository.GetByLeagueAsync(
            request.SleeperLeagueId, cancellationToken);

        return rosters
            .OrderBy(r => r.TeamName)
            .Select(r => new LeagueTeamSummaryDto(
                SleeperRosterId: r.SleeperRosterId,
                TeamName: r.TeamName ?? "Unknown Team",
                OwnerName: r.OwnerName ?? "Unknown Owner",
                Wins: r.Wins,
                Losses: r.Losses,
                PlayerCount: r.PlayerIds.Count))
            .ToList();
    }
}
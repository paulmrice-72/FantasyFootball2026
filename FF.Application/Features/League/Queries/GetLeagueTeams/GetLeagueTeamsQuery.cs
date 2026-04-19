using MediatR;

namespace FF.Application.Features.League.Queries.GetLeagueTeams;

public record GetLeagueTeamsQuery(string SleeperLeagueId) : IRequest<List<LeagueTeamSummaryDto>>;

public record LeagueTeamSummaryDto(
    string SleeperRosterId,
    string TeamName,
    string OwnerName,
    int Wins,
    int Losses,
    int PlayerCount);
// FF.Application/Features/Leagues/Queries/GetLeagueStandings/GetLeagueStandingsQuery.cs
using MediatR;

namespace FF.Application.Features.Leagues.Queries.GetLeagueStandings;

public record GetLeagueStandingsQuery(
    string SleeperLeagueId,
    int Season,
    int Week)
    : IRequest<LeagueStandingsDto?>;

public record LeagueStandingsDto(
    string SleeperLeagueId,
    int Season,
    int Week,
    List<TeamStandingDto> Teams);

public record TeamStandingDto(
    string SleeperRosterId,
    string TeamName,
    string OwnerName,
    int Wins,
    int Losses,
    int Ties,
    int WaiverPosition,
    int Rank,
    decimal ProjectedPointsThisWeek,
    decimal PointsFor,
    decimal PointsAgainst,
    string PlayoffProjection); // "In", "Out", "Bubble"
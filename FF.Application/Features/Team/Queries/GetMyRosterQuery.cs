using MediatR;

namespace FF.Application.Features.Team.Queries;

public record GetMyRosterQuery(string SleeperUserId, string SleeperLeagueId)
    : IRequest<MyRosterDto?>;

public record MyRosterDto(
    string TeamName,
    string OwnerName,
    string LeagueId,
    int Wins,
    int Losses,
    int WaiverPosition,
    List<MyRosterPlayerDto> Players);

public record MyRosterPlayerDto(
    string SleeperPlayerId,
    string PlayerName,
    string Position,
    string NflTeam,
    int? Age,
    string? InjuryDesignation,
    bool IsStarter,
    bool IsOnIr,
    bool IsOnTaxi,          // ← add
    double? MedianProjectedPoints,
    string? ByeWeek);
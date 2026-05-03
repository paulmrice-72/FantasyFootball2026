using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.Team.Queries;

public record GetMyRosterQuery(string SleeperUserId, string SleeperLeagueId)
    : IRequest<MyRosterDto?>;

public record MyRosterDto(
    string TeamName,
    string OwnerName,
    string? OwnerAvatar,
    string LeagueId,
    int Wins,
    int Losses,
    int WaiverPosition,
    List<MyRosterPlayerDto> Players,
    List<RosterPickDto> OwnedPicks);   // ← NEW

public record MyRosterPlayerDto(
    string SleeperPlayerId,
    string PlayerName,
    string Position,
    string NflTeam,
    int? Age,
    string? InjuryDesignation,
    bool IsStarter,
    bool IsOnIr,
    bool IsOnTaxi,
    double? MedianProjectedPoints,
    string? ByeWeek);
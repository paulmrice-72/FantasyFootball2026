// FF.Application/Features/Team/Queries/GetMyMatchupQuery.cs
using MediatR;

namespace FF.Application.Features.Team.Queries;

public record GetMyMatchupQuery(
    string SleeperUserId,
    string SleeperLeagueId,
    int Season,
    int Week)
    : IRequest<MyMatchupDto?>;

public record MyMatchupDto(
    int Week,
    int Season,
    MyMatchupSideDto MyTeam,
    MyMatchupSideDto Opponent,
    double MyWinProbability,
    double OpponentWinProbability);

public record MyMatchupSideDto(
    string TeamName,
    string OwnerName,
    double TotalProjectedPoints,
    double ProjectedFloor,
    double ProjectedCeiling,
    List<MyMatchupPlayerDto> Players);

public record MyMatchupPlayerDto(
    string SleeperPlayerId,
    string PlayerName,
    string Position,
    string NflTeam,
    bool IsStarter,
    double? MedianProjectedPoints,
    double? FloorProjectedPoints,
    double? CeilingProjectedPoints,
    string? InjuryDesignation);
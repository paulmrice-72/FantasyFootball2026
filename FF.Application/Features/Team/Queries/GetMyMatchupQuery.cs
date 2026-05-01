// FF.Application/Features/Team/Queries/GetMyMatchupQuery.cs
using FF.Application.Interfaces.Services;
using MediatR;

namespace FF.Application.Features.Team.Queries;

public record GetMyMatchupQuery(
    string SleeperUserId,
    string SleeperLeagueId,
    int Season,
    int Week) : IRequest<MyMatchupDto?>;

public record MyMatchupDto(
    int Week,
    int Season,
    string ScoringFormat,
    MyMatchupSideDto MyTeam,
    MyMatchupSideDto Opponent,
    double MyWinProbability,
    double OpponentWinProbability);

public record MyMatchupSideDto(
    string TeamName,
    string OwnerName,
    string? SleeperRosterId,
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
    string? SlotLabel,           // e.g. "QB", "FLEX", "BN" — from RosterConfig
    double? MedianProjectedPoints,
    double? FloorProjectedPoints,
    double? CeilingProjectedPoints,
    string? InjuryDesignation,
    Guid? LeagueId,
    string? ScoringFormat);
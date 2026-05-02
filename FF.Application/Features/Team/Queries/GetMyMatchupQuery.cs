// FF.Application/Features/Team/Queries/GetMyMatchupQuery.cs
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
    double OpponentWinProbability,
    double? MyActualPoints,         // NEW — non-null for completed past weeks
    double? OpponentActualPoints);  // NEW

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
    string? SlotLabel,
    double? MedianProjectedPoints,
    double? FloorProjectedPoints,
    double? CeilingProjectedPoints,
    double? BoomProbability,      // NEW — MATCHUP-003
    double? BustProbability,      // NEW — MATCHUP-003
    string? GameScript,           // NEW — MATCHUP-003
    string? OpponentTeam,         // NEW — MATCHUP-003
    double? ActualPoints,   // NEW — non-null for past weeks
    string? InjuryDesignation,
    Guid? LeagueId,
    string? ScoringFormat,
    ProjectionBreakdownDto? ProjectionBreakdown);

public record ProjectionBreakdownDto(
    double ProjectedPoints,
    double WeightedAvgPoints,
    double MatchupAdjustmentFactor,
    double SnapPctInput,
    double TargetShareInput,
    string GameScript,
    double SpreadInput,
    string ScoringFormat,
    int Season,
    int Week);
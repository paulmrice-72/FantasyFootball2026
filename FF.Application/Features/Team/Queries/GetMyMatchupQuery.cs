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
    // FAN-178. The player card resolves its simulation/projection/usage calls
    // by GSIS id (PlayerDetail: ApiPlayerId => GsisId ?? PlayerId), so a link
    // built from the Sleeper id alone loads the page and finds nothing.
    string? GsisId,
    string PlayerName,
    string Position,
    string NflTeam,
    bool IsStarter,
    string? SlotLabel,
    double? MedianProjectedPoints,
    // Expected points — arithmetic mean of the simulated distribution. Side totals
    // are built from THIS, not from MedianProjectedPoints: a matchup total is a sum,
    // and summing right-skewed medians understates it by 6-11% per position. Median
    // is retained per player because it is the honest "typical week" figure.
    double? MeanProjectedPoints,
    double? FloorProjectedPoints,
    double? CeilingProjectedPoints,
    double? BoomProbability,      // NEW — MATCHUP-003
    double? BustProbability,      // NEW — MATCHUP-003
    string? GameScript,           // NEW — MATCHUP-003
    string? OpponentTeam,         // NEW — MATCHUP-003
    // FAN-178. Null when the player has no game this week (bye, or a schedule
    // that has not been imported) — which is why this is bool? and not bool:
    // "away" and "no game at all" must not render the same.
    bool? IsHomeGame,
    // FAN-178. True once the player's game is final, which is what lets the
    // side total use banked actuals the way Sleeper does.
    bool IsGameFinal,
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
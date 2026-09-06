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
    // Expected points — arithmetic mean of the simulated distribution. This is
    // what the Roster tab's "Proj Pts" column shows, because that column is read
    // as "what will he score", which is an expectation. Median stays on the DTO:
    // it is the honest "typical week" figure and Start/Sit still ranks on it.
    double? MeanProjectedPoints,
    string? ByeWeek);
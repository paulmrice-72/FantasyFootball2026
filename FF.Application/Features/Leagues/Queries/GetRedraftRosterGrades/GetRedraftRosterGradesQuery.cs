// FF.Application/Features/Leagues/Queries/GetRedraftRosterGrades/GetRedraftRosterGradesQuery.cs
using MediatR;

namespace FF.Application.Features.Leagues.Queries.GetRedraftRosterGrades;

// FAN-107 (2026-08-30): redraft-specific counterpart to
// GetLeagueRosterGradesQuery. Deliberately a SEPARATE DTO rather than the
// dynasty DTO with fields nulled out (Paul's call) — keeps dynasty-only
// concepts (DynastyGrade/Score, TeamProfile, DraftCapitalScore,
// OwnedPickCount) out of redraft leagues entirely, and leaves room to add
// redraft-specific detail later without dragging dynasty fields along.
public record GetRedraftRosterGradesQuery(
    string SleeperLeagueId,
    int Season) : IRequest<RedraftLeagueRosterGradesDto?>;

public record RedraftLeagueRosterGradesDto(
    string SleeperLeagueId,
    int Season,
    List<RedraftTeamRosterGradeDto> Teams);

public record RedraftTeamRosterGradeDto(
    string SleeperRosterId,
    string TeamName,
    string OwnerName,
    int Rank,                    // 1 = strongest roster in the league — "where do I stand" ask
    string DepthGrade,           // A / B / C / D / F
    double DepthScore,           // 0–100 normalised, league-relative
    List<RedraftTeamAssetDto> TopAssets,
    List<TeamPositionGradeDto> PositionGrades);

public record RedraftTeamAssetDto(
    string PlayerName,
    string Position,
    double SeasonAvgPoints);      // sim-median projected points — no TradeValue/BreakoutScore (dynasty-only concepts)

/// <summary>
/// One position's standing for one team, so the league table can show the
/// positional breakdown inline instead of requiring a click into each team
/// (Paul's ask, 2026-09-01).
///
/// Graded the same way as the overall roster score on this page — ranked WITHIN
/// the league rather than against an absolute scale — so "B+ at WR" here means
/// "this league's 4th-best WR room", not "a good WR room in the abstract".
/// <paramref name="Placing"/> is the plain version of that and is usually the
/// more useful number to show: "3rd of 12".
/// </summary>
public record TeamPositionGradeDto(
    string Position,
    string Grade,                 // retained on the API; the UI shows Placing instead (2026-09-03)
    int Placing,                  // 1 = best room at this position in the league
    int TeamCount,
    double StarterPoints,         // avg sim median of the starters at this position
    int RosteredCount = 0,        // players at this position on the roster
    int ProjectedCount = 0);      // of those, how many actually have a projection

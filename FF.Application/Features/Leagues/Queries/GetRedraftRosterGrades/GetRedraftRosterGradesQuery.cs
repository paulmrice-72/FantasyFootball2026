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
    List<RedraftTeamAssetDto> TopAssets);

public record RedraftTeamAssetDto(
    string PlayerName,
    string Position,
    double SeasonAvgPoints);      // sim-median projected points — no TradeValue/BreakoutScore (dynasty-only concepts)

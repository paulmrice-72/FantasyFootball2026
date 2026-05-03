// FF.Application/Features/Team/Queries/GetPositionalDepthGradesQuery.cs
using MediatR;

namespace FF.Application.Features.Team.Queries;

public record GetPositionalDepthGradesQuery(
    string SleeperUserId,
    string SleeperLeagueId,
    int Season,
    string? SleeperRosterId = null)   // ← NEW: roster-id path for opponent lookups
    : IRequest<PositionalDepthGradesDto?>;

public record PositionalDepthGradesDto(
    List<PositionDepthGradeDto> Grades);

public record PositionDepthGradeDto(
    string Position,
    string Grade,
    int GradeScore,
    string Label,
    string Summary,
    int RosteredCount,
    int HealthyCount,
    double StarterScore,
    double DepthScore);
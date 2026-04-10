// FF.Application/Features/Team/Queries/GetPositionalDepthGradesQuery.cs
using MediatR;

namespace FF.Application.Features.Team.Queries;

public record GetPositionalDepthGradesQuery(
    string SleeperUserId,
    string SleeperLeagueId,
    int Season)
    : IRequest<PositionalDepthGradesDto?>;

public record PositionalDepthGradesDto(
    List<PositionDepthGradeDto> Grades);

public record PositionDepthGradeDto(
    string Position,          // QB, RB, WR, TE
    string Grade,             // A+, A, B, C, D, F
    int GradeScore,           // 0-100 for progress bar
    string Label,             // "Elite", "Strong", etc.
    string Summary,           // one-liner rationale
    int RosteredCount,        // how many at this pos
    int HealthyCount,         // excluding IR/Out
    double StarterScore,      // projected pts for starter(s)
    double DepthScore);       // weighted depth value
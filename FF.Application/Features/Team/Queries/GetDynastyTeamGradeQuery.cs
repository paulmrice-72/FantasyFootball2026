// FF.Application/Features/Team/Queries/GetDynastyTeamGradeQuery.cs
using MediatR;

namespace FF.Application.Features.Team.Queries;

public record GetDynastyTeamGradeQuery(
    string SleeperUserId,
    string SleeperLeagueId)
    : IRequest<DynastyTeamGradeDto?>;

public record DynastyTeamGradeDto(
    // Contention = win-now strength
    string ContentionGrade,      // A+..F
    int ContentionScore,      // 0-100
    string ContentionLabel,      // "Elite", "Strong", etc.
    string ContentionSummary,

    // Longevity = future dynasty outlook
    string LongevityGrade,
    int LongevityScore,
    string LongevityLabel,
    string LongevitySummary,

    // Supporting data
    string OverallProfile,       // e.g. "Win-Now", "Rebuilding", "Balanced"
    int RosteredCount,
    int PrimePlayerCount,
    int YoungPlayerCount,
    double AvgTradeValue,
    double AvgAge);
// FF.Application/Features/Team/Queries/GetStartSitRecommendationsQuery.cs
using MediatR;

namespace FF.Application.Features.Team.Queries;

public record GetStartSitRecommendationsQuery(
    string SleeperUserId,
    string SleeperLeagueId,
    int Season,
    int Week)
    : IRequest<StartSitRecommendationsDto?>;

public record StartSitRecommendationsDto(
    int Week,
    int Season,
    List<StartSitDecisionDto> Decisions);

/// <summary>
/// One decision group per position battle (e.g. "RB2 vs RB3 for FLEX").
/// </summary>
public record StartSitDecisionDto(
    string Position,
    string SlotLabel,           // e.g. "RB2", "FLEX", "WR3"
    List<StartSitOptionDto> Options);

public record StartSitOptionDto(
    string SleeperPlayerId,
    string PlayerName,
    string Position,
    string NflTeam,
    StartSitVerdict Verdict,    // Start, Sit, Lean Start, Lean Sit
    int ConfidenceScore,        // 0-100
    string ConfidenceLabel,     // "High", "Medium", "Low"
    double Median,
    double Floor,
    double Ceiling,
    double BoomProbability,
    double BustProbability,
    string? InjuryDesignation,
    string Rationale);          // one-line human-readable reason

public enum StartSitVerdict
{
    Start,
    LeanStart,
    LeanSit,
    Sit
}
// FF.Application/Features/Team/Queries/GetDraftPrepQuery.cs
using MediatR;

namespace FF.Application.Features.Team.Queries;

public record GetDraftPrepQuery(
    string SleeperUserId,
    string SleeperLeagueId,
    int SimSeason,
    int RookieSeason) : IRequest<DraftPrepDto?>;

public record DraftPrepDto(
    List<PositionNeedDto> PositionNeeds,
    List<RookieTargetDto> RookieTargets);

public record PositionNeedDto(
    string Position,
    string Grade,
    int GradeScore,
    string NeedLevel,
    string Summary);

// DRAFT-PARITY-001 (2026-05-07): extended to mirror Draft Board content.
// Source is now GetRookiePoolQuery so the two pages stay in lockstep.
public record RookieTargetDto(
    string SleeperPlayerId,
    string PlayerName,
    string Position,
    string? NflTeam,
    int? FantasyProsRank,           // nullable — pool may include rookies missing from FP CSV
    int? PositionRank,
    string? Tier,
    string NeedLevel,
    string FitLabel,
    double? ConsensusAdp,
    // Parity fields with Draft Board ─────────────────────────────────────
    int? Age,
    int? DraftRound,
    int? DraftPick,
    string? CollegeTeam,
    string? HeadshotUrl,
    double DynastyScore,
    int ScoreRank,                  // server-stamped rank by DynastyScore desc
    double? PffGrade,
    int? PffRank);
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

public record RookieTargetDto(
    string SleeperPlayerId,
    string PlayerName,
    string Position,
    string? NflTeam,
    int FantasyProsRank,
    int PositionRank,
    string? Tier,
    string NeedLevel,
    string FitLabel);
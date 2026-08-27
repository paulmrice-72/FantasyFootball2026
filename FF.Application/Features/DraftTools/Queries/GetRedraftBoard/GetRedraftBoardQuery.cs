// FF.Application/Features/DraftTools/Queries/GetRedraftBoard/GetRedraftBoardQuery.cs
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.DraftTools.Queries.GetRedraftBoard;

/// <summary>
/// FIX-PRESEASON-001 (2026-08-27): preseason / early-season redraft ranking
/// source, used as a fallback before real Week-N simulation data exists for
/// the current season (that pipeline needs THIS season's own game logs —
/// see CalculateProjectionsCommandHandler — which don't exist until games
/// are played).
///
/// Primary signal: live FFC redraft ADP (SyncRedraftAdpJob → redraftAdpCache)
/// for `Season` — drives inclusion and ordering, and naturally covers rookies
/// since real 2026 drafts are already happening industry-wide.
/// Secondary/context signal: prior-season (Season-1) per-game half-PPR
/// average from the season-average sim seed (SeedSeasonAverageSimsCommand,
/// stored as Week=0). Null for rookies — they have no prior-season NFL games.
///
/// Callers should try their normal Week-N simulation query first and only
/// fall back to this query when that comes back empty.
/// </summary>
public record GetRedraftBoardQuery(int Season, string? Position = null)
    : IRequest<Result<List<RedraftBoardEntryDto>>>;

public record RedraftBoardEntryDto(
    string SleeperPlayerId,
    string PlayerName,
    string Position,
    string? NflTeam,
    double Adp,
    int AdpRound,
    decimal? SeasonAvgPoints);

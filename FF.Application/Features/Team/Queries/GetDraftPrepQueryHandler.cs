// FF.Application/Features/Team/Queries/GetDraftPrepQueryHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Players.Queries.GetRookiePool;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Team.Queries;

// DRAFT-PARITY-001 (2026-05-07):
// Rewritten to consume GetRookiePoolQuery so Draft Prep and Draft Board
// share an identical rookie list, identical scoring, identical ordering.
// Position-need overlay (NeedLevel + FitLabel) is the only divergence.
public class GetDraftPrepQueryHandler(
    IMediator mediator,
    IRosterPlayerRepository rosterPlayerRepository,
    ILogger<GetDraftPrepQueryHandler> logger)
    : IRequestHandler<GetDraftPrepQuery, DraftPrepDto?>
{
    // Grade scores at or below this threshold = a positional Need
    private const int NeedThreshold = 40; // C+ or worse
    private const int StrengthThreshold = 65; // B+ or better

    // Only dynasty-relevant skill positions — excludes K, DST, OL, DL, LB, DB, etc.
    private static readonly HashSet<string> SkillPositions =
        new(StringComparer.OrdinalIgnoreCase) { "QB", "RB", "WR", "TE" };

    public async Task<DraftPrepDto?> Handle(
        GetDraftPrepQuery request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Computing draft prep for user {UserId} league {LeagueId}",
            request.SleeperUserId, request.SleeperLeagueId);

        // 1 — Get depth grades using sim season (drives Need/Strength/Neutral overlay)
        var depthGrades = await mediator.Send(
            new GetPositionalDepthGradesQuery(
                request.SleeperUserId,
                request.SleeperLeagueId,
                request.SimSeason),
            cancellationToken);

        // 2 — Build position needs from grades
        var positionNeeds = new List<PositionNeedDto>();

        if (depthGrades is not null)
        {
            foreach (var g in depthGrades.Grades)
            {
                var needLevel = g.GradeScore <= NeedThreshold ? "Need"
                              : g.GradeScore >= StrengthThreshold ? "Strength"
                              : "Neutral";

                positionNeeds.Add(new PositionNeedDto(
                    Position: g.Position,
                    Grade: g.Grade,
                    GradeScore: g.GradeScore,
                    NeedLevel: needLevel,
                    Summary: g.Summary));
            }
        }

        // 3 — Pull the canonical rookie pool (same source the Draft Board uses).
        //     This guarantees ordering / scoring parity between the two pages.
        var poolResult = await mediator.Send(
            new GetRookiePoolQuery(Position: null),
            cancellationToken);

        if (!poolResult.IsSuccess || poolResult.Value is null)
        {
            logger.LogWarning("Rookie pool query failed: {Error}", poolResult.Error);
            return new DraftPrepDto(
                PositionNeeds: positionNeeds,
                RookieTargets: []);
        }

        var pool = poolResult.Value;

        // 4 — Exclude already-rostered rookies (league-scoped — keep this filter)
        var leagueRosters = await rosterPlayerRepository
            .GetByLeagueAsync(request.SleeperLeagueId, cancellationToken);

        var rosteredIds = leagueRosters
            .SelectMany(r => r.PlayerIds ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 5 — Need lookup keyed by position
        var needLookup = positionNeeds
            .ToDictionary(n => n.Position, n => n.NeedLevel);

        // 6 — Project pool → RookieTargetDto, applying need overlay
        var targets = pool
            .Where(r => !string.IsNullOrEmpty(r.SleeperPlayerId)
                        && !rosteredIds.Contains(r.SleeperPlayerId)
                        && SkillPositions.Contains(r.Position))
            .Select(r =>
            {
                var needLevel = needLookup.TryGetValue(r.Position, out var nl)
                    ? nl : "Neutral";

                var fitLabel = needLevel == "Need" ? "Priority Pick"
                             : needLevel == "Strength" ? "Depth Only"
                             : "Good Value";

                return new RookieTargetDto(
                    SleeperPlayerId: r.SleeperPlayerId,
                    PlayerName: r.FullName,
                    Position: r.Position,
                    NflTeam: r.NflTeam,
                    FantasyProsRank: r.FantasyProsRank,
                    PositionRank: r.FantasyProsPositionRank,
                    Tier: r.FantasyProsTier,
                    NeedLevel: needLevel,
                    FitLabel: fitLabel,
                    ConsensusAdp: r.ConsensusAdp,
                    Age: r.Age,
                    DraftRound: r.DraftRound,
                    DraftPick: r.DraftPick,
                    CollegeTeam: r.CollegeTeam,
                    HeadshotUrl: r.HeadshotUrl,
                    DynastyScore: r.DynastyScore,
                    ScoreRank: r.ScoreRank,
                    PffGrade: r.PffGrade,
                    PffRank: r.PffRank);
            })
            // Need-tier first (Priority Picks float up), then FantasyPros rank asc.
            // FP rank is the trusted signal today; DynastyScore is a tiebreaker
            // until composite calibration converges (ρ ≥ 0.85 target).
            // Both columns are sortable client-side so the user can override.
            .OrderBy(r => r.NeedLevel == "Need" ? 0
                       : r.NeedLevel == "Neutral" ? 1 : 2)
            .ThenBy(r => r.FantasyProsRank ?? 999)
            .ThenByDescending(r => r.DynastyScore)
            .ToList();

        return new DraftPrepDto(
            PositionNeeds: positionNeeds,
            RookieTargets: targets);
    }
}
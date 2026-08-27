// FF.Application/Features/Team/Queries/GetDraftPrepQueryHandler.cs
using FF.Application.Features.DraftTools.Queries.GetRedraftBoard;
using FF.Application.Interfaces.Persistence;
using FF.Application.Players.Queries.GetRookiePool;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Team.Queries;

// DRAFT-PARITY-001 (2026-05-07):
// Rewritten to consume GetRookiePoolQuery so Draft Prep and Draft Board
// share an identical rookie list, identical scoring, identical ordering.
// Position-need overlay (NeedLevel + FitLabel) is the only divergence.
//
// Fix 2026-08-27c: this whole handler was rookie-pool-only regardless of
// league type — Paul flagged Draft Prep on his redraft league's My Roster
// page showing only rookies. Added a league-type branch: Dynasty keeps the
// exact rookie-pool behavior above; anything else (Redraft is the League
// entity's own default) now builds the target list from ALL non-rostered
// players via Week-1 simulation median — same stopgap signal used for the
// RookieDraftBoard redraft fix this session (see FAN-99). Longer-term this
// should share one source with the draft board rather than duplicating the
// "rank by Week-1 median" logic in two places — noted in FAN-100.
public class GetDraftPrepQueryHandler(
    IMediator mediator,
    IRosterPlayerRepository rosterPlayerRepository,
    ILeagueRepository leagueRepository,
    ISimulationResultRepository simulationResultRepository,
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

        // 3 — Exclude already-rostered players (league-scoped — keep this filter)
        var leagueRosters = await rosterPlayerRepository
            .GetByLeagueAsync(request.SleeperLeagueId, cancellationToken);

        var rosteredIds = leagueRosters
            .SelectMany(r => r.PlayerIds ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 4 — Need lookup keyed by position
        var needLookup = positionNeeds
            .ToDictionary(n => n.Position, n => n.NeedLevel);

        // 5 — League type decides the player pool. "Dynasty" is the explicit
        // special case (rookie pool); anything else — including an unknown
        // league — falls to the redraft/all-players branch, matching the
        // convention RookieDraftBoard.razor already uses
        // (_isRedraftMode = league?.LeagueType != "Dynasty").
        var league = await leagueRepository.GetBySleeperIdAsync(
            request.SleeperLeagueId, request.SimSeason, cancellationToken);

        List<RookieTargetDto> targets;

        if (league?.LeagueType == "Dynasty")
        {
            // Pull the canonical rookie pool (same source the Draft Board uses).
            // This guarantees ordering / scoring parity between the two pages.
            var poolResult = await mediator.Send(
                new GetRookiePoolQuery(Position: null),
                cancellationToken);

            if (!poolResult.IsSuccess || poolResult.Value is null)
            {
                logger.LogWarning("Rookie pool query failed: {Error}", poolResult.Error);
                return new DraftPrepDto(PositionNeeds: positionNeeds, RookieTargets: []);
            }

            targets = poolResult.Value
                .Where(r => !string.IsNullOrEmpty(r.SleeperPlayerId)
                            && !rosteredIds.Contains(r.SleeperPlayerId)
                            && SkillPositions.Contains(r.Position))
                .Select(r =>
                {
                    var needLevel = needLookup.TryGetValue(r.Position, out var nl) ? nl : "Neutral";
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
                .ToList();
        }
        else
        {
            // Redraft: no rookie-only concept applies — pool is every
            // non-rostered skill-position player, ranked by Week-1 simulation
            // median (same stopgap signal as the redraft draft board fix).
            // FantasyProsRank/Tier/ConsensusAdp/Age/Draft*/College/Pff* have
            // no equivalent from simulation data alone and are left null —
            // the DTO already treats all of these as optional.
            var simResults = await simulationResultRepository.GetByWeekAsync(
                request.SimSeason, 1, cancellationToken);

            if (simResults.Count > 0)
            {
                targets = simResults
                    .Where(s => !string.IsNullOrEmpty(s.SleeperPlayerId)
                                && !rosteredIds.Contains(s.SleeperPlayerId)
                                && SkillPositions.Contains(s.Position))
                    .OrderByDescending(s => s.Median)
                    .Select((s, i) =>
                    {
                        var needLevel = needLookup.TryGetValue(s.Position, out var nl) ? nl : "Neutral";
                        var fitLabel = needLevel == "Need" ? "Priority Pick"
                                     : needLevel == "Strength" ? "Depth Only"
                                     : "Good Value";

                        return new RookieTargetDto(
                            SleeperPlayerId: s.SleeperPlayerId!,
                            PlayerName: s.PlayerName,
                            Position: s.Position,
                            NflTeam: s.NflTeam,
                            FantasyProsRank: null,
                            PositionRank: null,
                            Tier: null,
                            NeedLevel: needLevel,
                            FitLabel: fitLabel,
                            ConsensusAdp: null,
                            Age: null,
                            DraftRound: null,
                            DraftPick: null,
                            CollegeTeam: null,
                            HeadshotUrl: null,
                            DynastyScore: (double)s.Median,
                            ScoreRank: i + 1,
                            PffGrade: null,
                            PffRank: null);
                    })
                    .ToList();
            }
            else
            {
                // FIX-PRESEASON-001 (2026-08-27): no Week-1 sim data yet
                // (preseason — that pipeline needs this season's own game
                // logs). Fall back to the live-ADP redraft board — same
                // source as the RookieDraftBoard.razor preseason fallback —
                // so Draft Prep isn't empty for a redraft league before Week
                // 1 is actually played. ADP drives rank/inclusion (covers
                // rookies); season-avg fills DynastyScore where it exists.
                var boardResult = await mediator.Send(
                    new GetRedraftBoardQuery(request.SimSeason), cancellationToken);

                var boardEntries = boardResult.IsSuccess && boardResult.Value is not null
                    ? boardResult.Value
                    : [];

                targets = boardEntries
                    .Where(a => !rosteredIds.Contains(a.SleeperPlayerId)
                                && SkillPositions.Contains(a.Position))
                    .OrderBy(a => a.Adp)
                    .Select((a, i) =>
                    {
                        var needLevel = needLookup.TryGetValue(a.Position, out var nl) ? nl : "Neutral";
                        var fitLabel = needLevel == "Need" ? "Priority Pick"
                                     : needLevel == "Strength" ? "Depth Only"
                                     : "Good Value";

                        return new RookieTargetDto(
                            SleeperPlayerId: a.SleeperPlayerId,
                            PlayerName: a.PlayerName,
                            Position: a.Position,
                            NflTeam: a.NflTeam,
                            FantasyProsRank: null,
                            PositionRank: null,
                            Tier: null,
                            NeedLevel: needLevel,
                            FitLabel: fitLabel,
                            ConsensusAdp: a.Adp,
                            Age: null,
                            DraftRound: null,
                            DraftPick: null,
                            CollegeTeam: null,
                            HeadshotUrl: null,
                            DynastyScore: a.SeasonAvgPoints.HasValue ? (double)a.SeasonAvgPoints.Value : 0,
                            ScoreRank: i + 1,
                            PffGrade: null,
                            PffRank: null);
                    })
                    .ToList();
            }
        }

        // Need-tier first (Priority Picks float up), then by score.
        // Both columns are sortable client-side so the user can override.
        // FIX-PRESEASON-002 (2026-08-27): tiebreak changed from DynastyScore
        // desc to ScoreRank asc. DynastyScore's UNITS differ per branch —
        // dynasty composite score, Week-1 median points, or (new tonight)
        // season-average points — so sorting on it directly broke ordering
        // for the ADP-fallback branch: raw next-year QB points dwarf RB/WR
        // points regardless of position value, so QBs with terrible ADP
        // (Mahomes ADP 102, Stafford ADP 77) floated to the top of Draft
        // Prep while Redraft Rankings (sorted by ADP directly) looked right.
        // ScoreRank is already "1 = best" within each branch's own natural
        // ordering (rookie pool score-desc, Week-1 median-desc, or ADP-asc —
        // see the DTO's own "server-stamped rank by DynastyScore desc"
        // comment for the dynasty case) so sorting by it directly is
        // equivalent to the old behavior for the two existing branches and
        // correct for the new ADP-fallback one.
        targets = targets
            .OrderBy(r => r.NeedLevel == "Need" ? 0
                       : r.NeedLevel == "Neutral" ? 1 : 2)
            .ThenBy(r => r.FantasyProsRank ?? 999)
            .ThenBy(r => r.ScoreRank)
            .ToList();

        return new DraftPrepDto(
            PositionNeeds: positionNeeds,
            RookieTargets: targets);
    }
}
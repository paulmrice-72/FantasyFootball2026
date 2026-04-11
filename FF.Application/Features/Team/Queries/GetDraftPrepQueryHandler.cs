// FF.Application/Features/Team/Queries/GetDraftPrepQueryHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Team.Queries;

public class GetDraftPrepQueryHandler(
    IMediator mediator,
    IFantasyProsRookieRankingRepository rookieRankingRepository,
    IRosterPlayerRepository rosterPlayerRepository,
    ILogger<GetDraftPrepQueryHandler> logger)
    : IRequestHandler<GetDraftPrepQuery, DraftPrepDto?>
{
    // Grade scores at or below this threshold = a positional Need
    private const int NeedThreshold = 40;    // C+ or worse
    private const int StrengthThreshold = 65; // B+ or better

    public async Task<DraftPrepDto?> Handle(
        GetDraftPrepQuery request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Computing draft prep for user {UserId} league {LeagueId}",
            request.SleeperUserId, request.SleeperLeagueId);

        // 1 — Get depth grades using sim season
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

        // 3 — Load all league rosters to exclude already-rostered players
        var leagueRosters = await rosterPlayerRepository
            .GetByLeagueAsync(request.SleeperLeagueId, cancellationToken);
        var rosteredIds = leagueRosters
            .SelectMany(r => r.PlayerIds ?? [])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 4 — Load rookie rankings using rookie season
        var rookies = await rookieRankingRepository
            .GetAllBySeasonAsync(request.RookieSeason, cancellationToken);

        // 5 — Build a need lookup keyed by position
        var needLookup = positionNeeds
            .ToDictionary(n => n.Position, n => n.NeedLevel);

        // 6 — Join: exclude rostered, tag with need level, sort
        //     Sort: Need positions first, then by FantasyProsRank
        var targets = rookies
            .Where(r => !string.IsNullOrEmpty(r.SleeperPlayerId)
                        && !rosteredIds.Contains(r.SleeperPlayerId))
            .Select(r =>
            {
                var needLevel = needLookup.TryGetValue(r.Position, out var nl)
                    ? nl : "Neutral";
                var fitLabel = needLevel == "Need" ? "Priority Pick"
                    : needLevel == "Strength" ? "Depth Only"
                    : "Good Value";

                return new RookieTargetDto(
                    SleeperPlayerId: r.SleeperPlayerId,
                    PlayerName: r.PlayerName,
                    Position: r.Position,
                    NflTeam: r.NflTeam,
                    FantasyProsRank: r.FantasyProsRank,
                    PositionRank: r.PositionRank,
                    Tier: r.Tier,
                    NeedLevel: needLevel,
                    FitLabel: fitLabel);
            })
            .OrderBy(r => r.NeedLevel == "Need" ? 0
                : r.NeedLevel == "Neutral" ? 1 : 2)
            .ThenBy(r => r.FantasyProsRank)
            .ToList();

        return new DraftPrepDto(
            PositionNeeds: positionNeeds,
            RookieTargets: targets);
    }
}
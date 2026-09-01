// FF.Application/Features/Leagues/Queries/GetRedraftRosterGrades/GetRedraftRosterGradesQueryHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Leagues.Queries.GetRedraftRosterGrades;

// FAN-107 (2026-08-30): redraft counterpart to GetLeagueRosterGradesQueryHandler.
// Shares the exact same roster-strength calculation (RosterStrengthCalculator) so
// the two handlers can't drift apart, but drops every dynasty-only concept —
// no DynastyScore, TeamProfile, DraftCapitalScore, or OwnedPickCount, since
// none of those mean anything in a one-year league.
//
// 2026-09-01: emits the per-position breakdown alongside the overall score, so
// the Standings table shows QB/RB/WR/TE standing per team without a click into
// each one. It costs nothing extra — the overall score was already the average
// of exactly these four numbers, they simply were not returned.
public class GetRedraftRosterGradesQueryHandler(
    IRosterPlayerRepository rosterPlayerRepository,
    IPlayerRepository playerRepository,
    ISimulationResultRepository simulationRepository,
    ILogger<GetRedraftRosterGradesQueryHandler> logger)
    : IRequestHandler<GetRedraftRosterGradesQuery, RedraftLeagueRosterGradesDto?>
{
    public async Task<RedraftLeagueRosterGradesDto?> Handle(
        GetRedraftRosterGradesQuery request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Building redraft roster grades for league {LeagueId} season {Season}",
            request.SleeperLeagueId, request.Season);

        var rosters = await rosterPlayerRepository
            .GetByLeagueAsync(request.SleeperLeagueId, cancellationToken);
        if (rosters.Count == 0) return null;

        var allPlayerIds = rosters.SelectMany(r => r.PlayerIds).Distinct().ToList();

        var simDocs = await simulationRepository
            .GetLatestBySleeperIdsAsync(allPlayerIds, request.Season, cancellationToken);
        var simLookup = simDocs
            .Where(s => s.SleeperPlayerId != null)
            .ToDictionary(s => s.SleeperPlayerId!, s => (double)s.Median);

        var players = await playerRepository.GetBySleeperIdsAsync(allPlayerIds, cancellationToken);
        var playerLookup = players
            .Where(p => p.SleeperPlayerId != null)
            .ToDictionary(p => p.SleeperPlayerId!, p => p);

        var rawTeams = rosters.Select(roster =>
        {
            // Per-position starter strength — the overall score is the average of
            // these four, so both come from one calculation and cannot disagree.
            var strengths = RosterStrengthCalculator.ComputePositionStrengths(
                roster.PlayerIds, playerLookup, simLookup);

            var depthScore = strengths.Count > 0
                ? strengths.Sum(s => s.NormalizedScore) / strengths.Count
                : 0.0;

            // Top assets, ranked by production RELATIVE TO POSITIONAL BASELINE
            // rather than by raw sim median.
            //
            // Raw median ranked quarterbacks first almost every time — they simply
            // score more points than any other position — so a backup QB could
            // appear as a team's headline asset ahead of a genuine WR1. Dividing by
            // the positional baseline makes "how far above a startable player at
            // his own position is he" the comparison, which is what a top asset
            // actually means.
            //
            // This is a proxy for value over replacement, not the real thing.
            // Proper replacement level comes from league roster shape and is L3
            // (FAN-118); when that lands, this ordering should use it instead.
            var topAssets = roster.PlayerIds
                .Where(id => simLookup.ContainsKey(id) && playerLookup.ContainsKey(id))
                .Select(id => (Id: id, Median: simLookup[id], Player: playerLookup[id]))
                .Where(x => RosterStrengthCalculator.GradedPositions
                    .Contains(x.Player.Position.ToString()))
                .Select(x => new
                {
                    x.Median,
                    x.Player,
                    Baseline = RosterStrengthCalculator.GetBaseline(x.Player.Position.ToString())
                })
                .OrderByDescending(x => x.Baseline > 0 ? x.Median / x.Baseline : 0)
                .Take(5)
                .Select(x => new RedraftTeamAssetDto(
                    PlayerName: x.Player.FullName ?? "Unknown",
                    Position: x.Player.Position.ToString() ?? "—",
                    SeasonAvgPoints: Math.Round(x.Median, 1)))
                .ToList();

            return (roster.SleeperRosterId, roster.TeamName, roster.OwnerName,
                DepthScore: depthScore, TopAssets: topAssets, Strengths: strengths);
        })
        .ToList();

        // Grade + rank relative to this league — same rationale as the
        // dynasty handler: self-correcting as the underlying score scale
        // moves, and it's what "where do I stand among the other teams"
        // actually means. See RosterStrengthCalculator.RankFractionToGrade.
        var depthRank = RosterStrengthCalculator.RankByDescending(rawTeams, t => t.DepthScore);

        // Each position is ranked across the league independently, so "B+ at WR"
        // means this league's 4th-best WR room rather than an absolute judgement.
        var teamCount = rawTeams.Count;
        var positionRanks = new Dictionary<string, (double[] Fractions, int[] Placings)>();

        foreach (var pos in RosterStrengthCalculator.GradedPositions)
        {
            var p = pos;

            var scores = rawTeams
                .Select(t => t.Strengths.FirstOrDefault(s => s.Position == p).NormalizedScore)
                .ToList();

            positionRanks[pos] = (
                RosterStrengthCalculator.RankByDescending(scores, x => x),
                RosterStrengthCalculator.PlacingByDescending(scores, x => x));
        }

        var teams = rawTeams
            .Select((t, idx) => new RedraftTeamRosterGradeDto(
                SleeperRosterId: t.SleeperRosterId,
                TeamName: t.TeamName,
                OwnerName: t.OwnerName,
                Rank: 0, // stamped for real just below, once sorted
                DepthGrade: RosterStrengthCalculator.RankFractionToGrade(depthRank[idx]),
                DepthScore: Math.Round(t.DepthScore, 1),
                TopAssets: t.TopAssets,
                PositionGrades: RosterStrengthCalculator.GradedPositions
                    .Select(pos => new TeamPositionGradeDto(
                        Position: pos,
                        Grade: RosterStrengthCalculator.RankFractionToGrade(
                            positionRanks[pos].Fractions[idx]),
                        Placing: positionRanks[pos].Placings[idx],
                        TeamCount: teamCount,
                        StarterPoints: Math.Round(
                            t.Strengths.FirstOrDefault(s => s.Position == pos).StarterPoints, 1)))
                    .ToList()))
            .OrderByDescending(t => t.DepthScore)
            .Select((t, idx) => t with { Rank = idx + 1 })
            .ToList();

        return new RedraftLeagueRosterGradesDto(
            SleeperLeagueId: request.SleeperLeagueId,
            Season: request.Season,
            Teams: teams);
    }
}

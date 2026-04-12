using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Leagues.Queries.GetLeagueRosterGrades;

public class GetLeagueRosterGradesQueryHandler(
    IRosterPlayerRepository rosterPlayerRepository,
    IDynastyValuationRepository dynastyValuationRepository,
    IPlayerRepository playerRepository,
    ILogger<GetLeagueRosterGradesQueryHandler> logger)
    : IRequestHandler<GetLeagueRosterGradesQuery, LeagueRosterGradesDto?>
{
    public async Task<LeagueRosterGradesDto?> Handle(
        GetLeagueRosterGradesQuery request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Building roster grades for league {LeagueId} season {Season}",
            request.SleeperLeagueId, request.Season);

        // 1 — Load all rosters for this league
        var rosters = await rosterPlayerRepository
            .GetByLeagueAsync(request.SleeperLeagueId, cancellationToken);

        if (rosters.Count == 0)
            return null;

        // 2 — Collect all player IDs across all rosters
        var allPlayerIds = rosters
            .SelectMany(r => r.PlayerIds)
            .Distinct()
            .ToList();

        // 3 — Bulk load dynasty valuations + player records
        var valuations = await dynastyValuationRepository
            .GetBySleeperPlayerIdsAsync(allPlayerIds, cancellationToken);

        var valuationLookup = valuations
            .ToDictionary(v => v.SleeperPlayerId, v => v);

        var players = await playerRepository
            .GetBySleeperIdsAsync(allPlayerIds, cancellationToken);

        var playerLookup = players
            .Where(p => p.SleeperPlayerId != null)
            .ToDictionary(p => p.SleeperPlayerId!, p => p);

        // 4 — Grade each team
        var teams = rosters.Select(roster =>
        {
            // Get dynasty valuations for all players on this roster
            var rosterValuations = roster.PlayerIds
                .Where(id => valuationLookup.ContainsKey(id))
                .Select(id => (Id: id, Val: valuationLookup[id]))
                .ToList();

            // ── Depth Score — top 15 starters by trade value ──────────
            var topByTradeValue = rosterValuations
                .OrderByDescending(x => x.Val.TradeValue)
                .Take(15)
                .ToList();

            var depthScore = topByTradeValue.Count > 0
                ? topByTradeValue.Average(x => x.Val.TradeValue)
                : 0.0;

            // ── Dynasty Score — weighted blend ─────────────────────────
            // TradeValue 50% + BreakoutScore 30% + YearsOfPrime 20%
            // YearsOfPrime normalized: 10 years = 100 pts
            var dynastyScore = rosterValuations.Count > 0
                ? rosterValuations.Average(x =>
                    (x.Val.TradeValue * 0.50) +
                    (x.Val.BreakoutScore * 0.30) +
                    (Math.Min(x.Val.YearsOfPrimeRemaining, 10) * 10.0 * 0.20))
                : 0.0;

            // ── Top assets (top 5 by trade value) ─────────────────────
            var topAssets = topByTradeValue
                .Take(5)
                .Select(x =>
                {
                    playerLookup.TryGetValue(x.Id, out var p);
                    return new TeamAssetDto(
                        PlayerName: p?.FullName ?? "Unknown",
                        Position: p?.Position.ToString() ?? "—",
                        TradeValue: Math.Round(x.Val.TradeValue, 1),
                        BreakoutScore: Math.Round(x.Val.BreakoutScore, 1),
                        Age: p?.Age);
                })
                .ToList();

            return new TeamRosterGradeDto(
                SleeperRosterId: roster.SleeperRosterId,
                TeamName: roster.TeamName,
                OwnerName: roster.OwnerName,
                DepthGrade: ScoreToGrade(depthScore),
                DepthScore: Math.Round(depthScore, 1),
                DynastyGrade: ScoreToGrade(dynastyScore),
                DynastyScore: Math.Round(dynastyScore, 1),
                TopAssets: topAssets);
        })
        .OrderByDescending(t => t.DepthScore)
        .ToList();

        return new LeagueRosterGradesDto(
            SleeperLeagueId: request.SleeperLeagueId,
            Season: request.Season,
            Teams: teams);
    }

    private static string ScoreToGrade(double score) => score switch
    {
        >= 70 => "A",
        >= 55 => "B",
        >= 40 => "C",
        >= 25 => "D",
        _ => "F"
    };
}
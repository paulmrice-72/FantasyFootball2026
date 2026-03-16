using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace FF.Infrastructure.Services;

public class DefensiveRankingService(
    IPlayerGameLogRepository gameLogRepository,
    IDefensiveRankingRepository defensiveRankingRepository,
    ILogger<DefensiveRankingService> logger) : IDefensiveRankingService
{
    private static readonly string[] TrackedPositions = ["QB", "RB", "WR", "TE"];

    public async Task CalculateAsync(int season, int throughWeek, CancellationToken ct = default)
    {
        logger.LogInformation(
            "DefensiveRankingService starting for season {Season} through week {Week}",
            season, throughWeek);

        // Pull all regular season game logs up to throughWeek
        var logs = await gameLogRepository.GetBySeasonAsync(season, ct);
        var regLogs = logs
            .Where(x => x.SeasonType == "REG"
                     && x.Week >= 1
                     && x.Week <= throughWeek
                     && !string.IsNullOrEmpty(x.OpponentTeam)
                     && !string.IsNullOrEmpty(x.Position)
                     && TrackedPositions.Contains(x.Position))
            .ToList();

        if (regLogs.Count == 0)
        {
            logger.LogWarning("No game logs found for season {Season} through week {Week}",
                season, throughWeek);
            return;
        }

        var documents = new List<DefensiveRankingDocument>();
        var l4wMinWeek = Math.Max(1, throughWeek - 3); // last 4 weeks

        foreach (var position in TrackedPositions)
        {
            var posLogs = regLogs.Where(x => x.Position == position).ToList();

            // Season averages per defending team
            var seasonGroups = posLogs
                .GroupBy(x => x.OpponentTeam)
                .Select(g => new
                {
                    Team = g.Key,
                    AvgAllowed = g.Average(x => (double)(x.FantasyPointsPpr > 0 ? x.FantasyPointsPpr : x.FantasyPoints)),
                    GamesAllowed = g.Select(x => x.Week).Distinct().Count()
                })
                .ToList();

            // L4W averages per defending team
            var l4wGroups = posLogs
                .Where(x => x.Week >= l4wMinWeek)
                .GroupBy(x => x.OpponentTeam)
                .Select(g => new
                {
                    Team = g.Key,
                    AvgAllowedL4W = g.Average(x => (double)(x.FantasyPointsPpr > 0 ? x.FantasyPointsPpr : x.FantasyPoints))
                })
                .ToDictionary(x => x.Team ?? string.Empty, x => x.AvgAllowedL4W);

            // Percentile rank within position group
            var seasonRanked = seasonGroups
                .OrderBy(x => x.AvgAllowed)
                .ToList();

            var teamCount = seasonRanked.Count;

            for (var index = 0; index < teamCount; index++)
            {
                var entry = seasonRanked[index];

                var seasonPct = teamCount > 1
                    ? Math.Round((decimal)index / (teamCount - 1) * 100, 1)
                    : 50m;

                l4wGroups.TryGetValue(entry.Team ?? string.Empty, out var avgL4W);

                // L4W percentile — rank within teams that have L4W data
                var l4wRanked = l4wGroups.OrderBy(x => x.Value).ToList();
                var l4wIndex = l4wRanked.FindIndex(x => x.Key == entry.Team);
                var l4wPct = l4wIndex >= 0 && l4wRanked.Count > 1
                    ? Math.Round((decimal)l4wIndex / (l4wRanked.Count - 1) * 100, 1)
                    : seasonPct; // fall back to season percentile if no L4W data

                // Composite: 40% season, 60% recent
                var difficultyScore = Math.Round(seasonPct * 0.4m + l4wPct * 0.6m, 1);

                documents.Add(new DefensiveRankingDocument
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    Team = entry.Team ?? string.Empty,
                    Position = position,
                    Season = season,
                    Week = throughWeek,
                    AvgFantasyPointsAllowed = Math.Round((decimal)entry.AvgAllowed, 2),
                    AvgFantasyPointsAllowedL4W = Math.Round((decimal)avgL4W, 2),
                    SeasonPercentile = seasonPct,
                    L4WPercentile = l4wPct,
                    DifficultyScore = difficultyScore,
                    GamesAllowed = entry.GamesAllowed,
                    CalculatedAt = DateTime.UtcNow
                });
            }
        }

        logger.LogInformation(
            "Calculated {Count} defensive ranking documents for season {Season} week {Week}",
            documents.Count, season, throughWeek);

        await defensiveRankingRepository.UpsertBatchAsync(documents, ct);

        logger.LogInformation("DefensiveRankingService complete");
    }
}
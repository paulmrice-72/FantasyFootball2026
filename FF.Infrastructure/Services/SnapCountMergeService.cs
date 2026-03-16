using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

public class SnapCountMergeService(
    ISnapCountRepository snapCountRepository,
    IPlayerGameLogRepository playerGameLogRepository,
    ILogger<SnapCountMergeService> logger
) : ISnapCountMergeService
{
    public async Task<SnapCountMergeResult> MergeAsync(
        int season,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting snap count merge for season {Season}", season);

        try
        {
            // Load all snap counts for the season
            var allSnapCounts = new List<FF.Domain.Documents.SnapCountDocument>();
            for (int week = 1; week <= 18; week++)
            {
                var weekSnaps = await snapCountRepository.GetBySeasonWeekAsync(
                    season, week, cancellationToken);
                allSnapCounts.AddRange(weekSnaps);
            }

            if (allSnapCounts.Count == 0)
            {
                logger.LogWarning("No snap counts found for season {Season}", season);
                return new SnapCountMergeResult(false, 0, 0,
                    $"No snap counts found for season {season}.");
            }

            // Build lookup: PlayerName + Team + Season + Week
            var snapLookup = allSnapCounts
                .GroupBy(s => MakeKey(s.PlayerName, s.Team, s.Season, s.Week))
                .ToDictionary(g => g.Key, g => g.First());

            logger.LogInformation("Loaded {Count} snap count records into lookup",
                snapLookup.Count);

            // Get all game logs for the season
            var gameLogs = await playerGameLogRepository.GetBySeasonAsync(
                season, cancellationToken);

            int merged = 0;
            int unmatched = 0;

            foreach (var log in gameLogs)
            {
                var key = MakeKey(log.PlayerName, log.NflTeam, log.Season, log.Week);
                if (snapLookup.TryGetValue(key, out var snap))
                {
                    log.OffenseSnaps = snap.OffenseSnaps;
                    log.SnapPct = snap.OffensePct;
                    merged++;
                }
                else
                {
                    unmatched++;
                }
            }

            // Persist updated game logs
            await playerGameLogRepository.BulkUpdateSnapCountsAsync(gameLogs, cancellationToken);

            logger.LogInformation(
                "Snap count merge complete. Merged: {Merged}, Unmatched: {Unmatched}",
                merged, unmatched);

            return new SnapCountMergeResult(true, merged, unmatched, null);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Snap count merge failed for season {Season}", season);
            return new SnapCountMergeResult(false, 0, 0, ex.Message);
        }
    }

    private static string MakeKey(string playerName, string team, int season, int week)
        => $"{playerName.Trim().ToLower()}|{team.Trim().ToUpper()}|{season}|{week}";
}
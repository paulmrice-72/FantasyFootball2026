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

            // Build lookup: PlayerName + Team + Season + Week.
            // The snap count side is nflverse's `player` column, which is the FULL name
            // ("Josh Allen").
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
            var unmatchedSamples = new List<string>();

            foreach (var log in gameLogs)
            {
                // PlayerGameLogDocument.PlayerName comes from nflverse `player_name`,
                // which is ABBREVIATED ("J.Allen"). The snap count side is the full name.
                // Keying on PlayerName therefore matched exactly nothing, every run, since
                // the feature shipped. DisplayName holds `player_display_name` — the full
                // name — and is the correct join key. See FAN-122.
                var name = !string.IsNullOrWhiteSpace(log.DisplayName)
                    ? log.DisplayName
                    : log.PlayerName;

                var key = MakeKey(name, log.NflTeam, log.Season, log.Week);

                if (snapLookup.TryGetValue(key, out var snap))
                {
                    log.OffenseSnaps = snap.OffenseSnaps;
                    log.SnapPct = snap.OffensePct;
                    merged++;
                }
                else
                {
                    unmatched++;
                    if (unmatchedSamples.Count < 20)
                        unmatchedSamples.Add(key);
                }
            }

            // Persist updated game logs
            await playerGameLogRepository.BulkUpdateSnapCountsAsync(gameLogs, cancellationToken);

            if (merged == 0)
            {
                logger.LogError(
                    "Snap count merge for season {Season} matched NOTHING against {LogCount} "
                    + "game logs and {SnapCount} snap rows. Sample unmatched keys: {Samples}",
                    season, gameLogs.Count, snapLookup.Count, unmatchedSamples);
            }
            else
            {
                logger.LogInformation(
                    "Snap count merge complete. Merged: {Merged}, Unmatched: {Unmatched}",
                    merged, unmatched);

                // Team abbreviation drift and mid-season trades (NflTeam is nflverse
                // `recent_team`, not the team for that week's game) both land here.
                if (unmatched > 0)
                {
                    logger.LogWarning(
                        "Snap count merge for season {Season} left {Unmatched} game logs "
                        + "unmatched. Sample keys: {Samples}",
                        season, unmatched, unmatchedSamples);
                }
            }

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

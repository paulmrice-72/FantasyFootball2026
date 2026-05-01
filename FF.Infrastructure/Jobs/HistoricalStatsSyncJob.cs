// FF.Infrastructure/Jobs/HistoricalStatsSyncJob.cs
using FF.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

public class HistoricalStatsSyncJob(
    IHistoricalStatsImportService importService,
    INflverseDownloadService downloadService,
    IPlayerIdResolutionService resolutionService,
    INflContextService nflContext,
    ILogger<HistoricalStatsSyncJob> logger)
{
    // Hangfire-safe no-arg entry point
    public Task RunAsync() => SyncCurrentSeasonAsync();

    public async Task SyncCurrentSeasonAsync(int? season = null)
    {
        var currentSeason = season ?? await nflContext.GetSeasonAsync();

        logger.LogInformation("Hangfire weekly sync starting for season {Season}", currentSeason);

        try
        {
            var download = await downloadService.DownloadCurrentSeasonAsync(currentSeason);
            if (!download.Success)
            {
                logger.LogError("nflverse download failed for season {Season}: {Error}",
                    currentSeason, download.ErrorMessage);
                throw new Exception($"nflverse download failed: {download.ErrorMessage}");
            }

            logger.LogInformation("Downloaded player_stats_{Season}.csv — {Size:N0} bytes",
                currentSeason, download.FileSizeBytes);

            var result = await importService.ImportSeasonAsync(currentSeason);
            logger.LogInformation(
                "Hangfire weekly sync complete. Season {Season}: {Inserted} inserted, {Replaced} replaced, duration={Duration}",
                currentSeason, result.TotalInserted, result.TotalReplaced, result.Duration);

            logger.LogInformation("Running SleeperPlayerId backfill after season {Season} import", currentSeason);
            var resolution = await resolutionService.BackfillMissingSleeperIdsAsync();
            logger.LogInformation("SleeperPlayerId backfill complete — Resolved: {Resolved}, Unresolved: {Unresolved}",
                resolution.Resolved, resolution.Unresolved);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Hangfire weekly sync FAILED for season {Season}", currentSeason);
            throw;
        }
    }
}
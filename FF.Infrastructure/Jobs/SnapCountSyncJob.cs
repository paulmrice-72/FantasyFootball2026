using FF.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

public class SnapCountSyncJob(
    ISnapCountImportService snapCountImportService,
    ISnapCountMergeService snapCountMergeService,
    ILogger<SnapCountSyncJob> logger)
{
    /// <summary>
    /// Imports and merges snap counts. Pass a season to backfill a prior year;
    /// omit it (the Hangfire recurring path) to use the calendar season.
    /// </summary>
    public async Task RunAsync(int? season = null)
    {
        int targetSeason = season ?? GetCurrentNflSeason();
        logger.LogInformation(
            "SnapCountSyncJob starting for season {Season} ({Source})",
            targetSeason, season.HasValue ? "explicit" : "calendar");

        var importResult = await snapCountImportService.ImportAsync(targetSeason);
        if (!importResult.Success)
        {
            logger.LogError("Snap count import failed: {Error}", importResult.ErrorMessage);
            return;
        }

        if (importResult.Inserted == 0 && importResult.Replaced == 0)
        {
            logger.LogWarning(
                "Snap count import for season {Season} wrote NOTHING — 0 inserted, 0 replaced.",
                targetSeason);
        }
        else
        {
            logger.LogInformation("Import complete. Inserted: {Inserted}, Replaced: {Replaced}",
                importResult.Inserted, importResult.Replaced);
        }

        var mergeResult = await snapCountMergeService.MergeAsync(targetSeason);
        if (!mergeResult.Success)
        {
            logger.LogError("Snap count merge failed: {Error}", mergeResult.ErrorMessage);
            return;
        }

        if (mergeResult.Merged == 0)
        {
            logger.LogWarning(
                "Snap count merge for season {Season} matched NOTHING — 0 merged, {Unmatched} unmatched.",
                targetSeason, mergeResult.Unmatched);
        }
        else
        {
            logger.LogInformation(
                "Merge complete. Merged: {Merged}, Unmatched: {Unmatched}",
                mergeResult.Merged, mergeResult.Unmatched);
        }
    }

    private static int GetCurrentNflSeason()
    {
        var now = DateTime.UtcNow;
        return now.Month >= 3 ? now.Year : now.Year - 1;
    }
}

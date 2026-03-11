using FF.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

public class SnapCountSyncJob(
    ISnapCountImportService snapCountImportService,
    ISnapCountMergeService snapCountMergeService,
    ILogger<SnapCountSyncJob> logger)
{
    public async Task RunAsync(int season)
    {
        logger.LogInformation("SnapCountSyncJob starting for season {Season}", season);

        var importResult = await snapCountImportService.ImportAsync(season);
        if (!importResult.Success)
        {
            logger.LogError("Snap count import failed: {Error}", importResult.ErrorMessage);
            return;
        }

        logger.LogInformation("Import complete. Inserted: {Inserted}, Replaced: {Replaced}",
            importResult.Inserted, importResult.Replaced);

        var mergeResult = await snapCountMergeService.MergeAsync(season);
        if (!mergeResult.Success)
        {
            logger.LogError("Snap count merge failed: {Error}", mergeResult.ErrorMessage);
            return;
        }

        logger.LogInformation(
            "Merge complete. Merged: {Merged}, Unmatched: {Unmatched}",
            mergeResult.Merged, mergeResult.Unmatched);
    }
}
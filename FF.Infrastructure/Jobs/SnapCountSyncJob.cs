using FF.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

/// <summary>
/// Outcome of a snap count sync. Returned rather than swallowed so an admin trigger
/// can report a failure instead of answering 200 with "complete".
/// </summary>
public record SnapCountSyncResult(
    bool Success,
    int Season,
    int Inserted,
    int Replaced,
    int Merged,
    int Unmatched,
    string? Error);

public class SnapCountSyncJob(
    ISnapCountImportService snapCountImportService,
    ISnapCountMergeService snapCountMergeService,
    ILogger<SnapCountSyncJob> logger)
{
    // Truncated because a Mongo bulk-write failure can carry one WriteError per row —
    // 26,000+ of them on a full season, which is not a thing to put in an HTTP response.
    private const int MaxErrorLength = 2000;

    /// <summary>
    /// Hangfire recurring entry point. Separate from RunAsync because Hangfire's
    /// AddOrUpdate expects Expression&lt;Func&lt;T, Task&gt;&gt; and expression trees are
    /// awkward about both Task&lt;T&gt; bodies and omitted optional arguments.
    /// </summary>
    public async Task RunRecurringAsync() => await RunAsync(null);

    /// <summary>
    /// Imports and merges snap counts. Pass a season to backfill a prior year;
    /// omit it to use the calendar season.
    /// </summary>
    public async Task<SnapCountSyncResult> RunAsync(int? season = null)
    {
        int targetSeason = season ?? GetCurrentNflSeason();
        logger.LogInformation(
            "SnapCountSyncJob starting for season {Season} ({Source})",
            targetSeason, season.HasValue ? "explicit" : "calendar");

        var importResult = await snapCountImportService.ImportAsync(targetSeason);
        if (!importResult.Success)
        {
            logger.LogError("Snap count import failed: {Error}", importResult.ErrorMessage);
            return new SnapCountSyncResult(
                false, targetSeason, 0, 0, 0, 0, Truncate(importResult.ErrorMessage));
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
            return new SnapCountSyncResult(
                false, targetSeason, importResult.Inserted, importResult.Replaced,
                0, 0, Truncate(mergeResult.ErrorMessage));
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

        return new SnapCountSyncResult(
            true, targetSeason,
            importResult.Inserted, importResult.Replaced,
            mergeResult.Merged, mergeResult.Unmatched, null);
    }

    private static string? Truncate(string? message) =>
        message is null || message.Length <= MaxErrorLength
            ? message
            : message[..MaxErrorLength] + $"… [truncated, {message.Length} chars total]";

    private static int GetCurrentNflSeason()
    {
        var now = DateTime.UtcNow;
        return now.Month >= 3 ? now.Year : now.Year - 1;
    }
}

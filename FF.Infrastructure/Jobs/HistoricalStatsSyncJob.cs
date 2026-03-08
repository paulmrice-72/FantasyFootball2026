// FF.Infrastructure/Jobs/HistoricalStatsSyncJob.cs
using FF.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;
using System.Drawing;
using System.Reflection.Metadata;

namespace FF.Infrastructure.Jobs;

public class HistoricalStatsSyncJob(
    IHistoricalStatsImportService importService,
    ILogger<HistoricalStatsSyncJob> logger)
{
    private readonly IHistoricalStatsImportService _importService = importService;
    private readonly ILogger<HistoricalStatsSyncJob> _logger = logger;

    public async Task SyncCurrentSeasonAsync()
    {
        var currentSeason = GetCurrentNflSeason();

        _logger.LogInformation(
            "Hangfire weekly sync starting for season {Season}", currentSeason);

        try
        {
            var result = await _importService.ImportSeasonAsync(currentSeason);

            _logger.LogInformation(
                "Hangfire weekly sync complete. Season {Season}: " +
                "{Inserted} inserted, {Replaced} replaced, duration={Duration}",
                currentSeason,
                result.TotalInserted,
                result.TotalReplaced,
                result.Duration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Hangfire weekly sync FAILED for season {Season}", currentSeason);
            throw; // Re-throw — Hangfire marks as Failed and retries
        }
    }

    private static int GetCurrentNflSeason()
    {
        var now = DateTime.UtcNow;
        // NFL season runs Sep–Feb. If before March, we're still in prior season.
        return now.Month >= 3 ? now.Year : now.Year - 1;
    }
}
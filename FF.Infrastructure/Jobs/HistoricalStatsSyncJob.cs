// FF.Infrastructure/Jobs/HistoricalStatsSyncJob.cs
using FF.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs
{
    public class HistoricalStatsSyncJob(
        IHistoricalStatsImportService importService,
        INflverseDownloadService downloadService,
        ILogger<HistoricalStatsSyncJob> logger)
    {
        private readonly IHistoricalStatsImportService _importService = importService;
        private readonly INflverseDownloadService _downloadService = downloadService;
        private readonly ILogger<HistoricalStatsSyncJob> _logger = logger;

        public async Task SyncCurrentSeasonAsync()
        {
            var currentSeason = GetCurrentNflSeason();

            _logger.LogInformation(
                "Hangfire weekly sync starting for season {Season}", currentSeason);

            try
            {
                // Step 1 — Download latest CSV from nflverse
                var download = await _downloadService
                    .DownloadCurrentSeasonAsync(currentSeason);

                if (!download.Success)
                {
                    _logger.LogError(
                        "nflverse download failed for season {Season}: {Error}",
                        currentSeason, download.ErrorMessage);
                    throw new Exception(
                        $"nflverse download failed: {download.ErrorMessage}");
                }

                _logger.LogInformation(
                    "Downloaded player_stats_{Season}.csv — {Size:N0} bytes",
                    currentSeason, download.FileSizeBytes);

                // Step 2 — Import the downloaded CSV into MongoDB
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
                throw;
            }
        }

        private static int GetCurrentNflSeason()
        {
            var now = DateTime.UtcNow;
            return now.Month >= 3 ? now.Year : now.Year - 1;
        }
    }
}
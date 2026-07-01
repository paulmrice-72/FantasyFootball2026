// FF.Infrastructure/Jobs/EspnStatsSyncJob.cs
using FF.Application.Interfaces.Services;
using FF.Infrastructure.Services;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

/// <summary>
/// Hangfire wrapper for the two-phase ESPN stats pipeline.
///
/// Phase 1 (SyncEspnIdsAsync): One-time run — bridges GsisId → EspnId via
///   nflverse players.csv. Re-run on-demand if new players are added.
///
/// Phase 2 (SyncStatsAsync): Fetches 2025 season stats from ESPN API and
///   seeds simulation_results with Week=0 season-average sentinels.
///   Run on-demand; registered as recurring weekly once the season starts.
///
/// Both jobs registered as on-demand only in Program.cs (no recurring schedule
/// during the offseason). Trigger via AdminController endpoints.
/// </summary>
public class EspnStatsSyncJob(
    IEspnStatsSyncService syncService,
    ILogger<EspnStatsSyncJob> logger)
{
    [AutomaticRetry(Attempts = 2)]
    public async Task SyncEspnIdsAsync()
    {
        logger.LogInformation("Hangfire: Starting EspnId bridge job");

        var result = await syncService.SyncEspnIdsAsync(CancellationToken.None);

        logger.LogInformation(
            "Hangfire: EspnId bridge complete — Matched: {Matched}, Skipped: {Skipped}, AlreadyHad: {Already}, Duration: {Duration:0.0}s",
            result.PlayersMatched,
            result.PlayersSkipped,
            result.PlayersAlreadyHadId,
            result.Duration.TotalSeconds);
    }

    [AutomaticRetry(Attempts = 2)]
    public async Task SyncStatsAsync(int season = 2025)
    {
        logger.LogInformation("Hangfire: Starting ESPN stats sync for season {Season}", season);

        var result = await syncService.SyncStatsAsync(season, CancellationToken.None);

        if (result.PlayersFailed > 0)
        {
            logger.LogWarning(
                "Hangfire: ESPN stats sync finished with failures — " +
                "Processed: {P}, Upserted: {U}, Failed: {F}, NoEspnId: {N}, Duration: {D:0.0}s",
                result.PlayersProcessed,
                result.PlayersUpserted,
                result.PlayersFailed,
                result.PlayersSkippedNoEspnId,
                result.Duration.TotalSeconds);
        }
        else
        {
            logger.LogInformation(
                "Hangfire: ESPN stats sync complete — " +
                "Processed: {P}, Upserted: {U}, NoEspnId: {N}, Duration: {D:0.0}s",
                result.PlayersProcessed,
                result.PlayersUpserted,
                result.PlayersSkippedNoEspnId,
                result.Duration.TotalSeconds);
        }
    }
}
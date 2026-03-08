// FF.Infrastructure/Jobs/LeagueSyncJob.cs
//
// Hangfire background job that syncs all imported leagues weekly.
// Runs every Tuesday after NFL games (Monday Night Football finishes Monday).
// Tuesday gives time for Sleeper to finalize all stats and transactions.

using FF.Application.Interfaces.Services;
using Hangfire;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

public class LeagueSyncJob(
    ISleeperLeagueImportService importService,
    FF.Infrastructure.Persistence.SQL.FFDbContext dbContext,
    ILogger<LeagueSyncJob> logger)
{
    private readonly ISleeperLeagueImportService _importService = importService;
    private readonly FF.Infrastructure.Persistence.SQL.FFDbContext _dbContext = dbContext;
    private readonly ILogger<LeagueSyncJob> _logger = logger;

    /// <summary>
    /// Syncs all active leagues currently in the database.
    /// Scheduled to run every Tuesday at 10am via Hangfire cron.
    /// Register in DependencyInjection.cs:
    ///   RecurringJob.AddOrUpdate{LeagueSyncJob}(
    ///       "league-sync-weekly",
    ///       job => job.SyncAllLeaguesAsync(),
    ///       "0 10 * * 2");  // 10am every Tuesday
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    public async Task SyncAllLeaguesAsync()
    {
        _logger.LogInformation("Starting weekly league sync job");

        // Get all active leagues from DB
        var activeLeagues = _dbContext.Leagues
            .Where(l => l.IsActive)
            .Select(l => l.SleeperLeagueId)
            .ToList();

        if (activeLeagues.Count == 0)
        {
            _logger.LogInformation("No active leagues found to sync");
            return;
        }

        _logger.LogInformation("Syncing {Count} active leagues", activeLeagues.Count);

        var succeeded = 0;
        var failed = 0;

        foreach (var leagueId in activeLeagues)
        {
            try
            {
                await _importService.SyncLeagueAsync(leagueId);
                succeeded++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to sync league {LeagueId}", leagueId);
                failed++;
                // Continue with remaining leagues even if one fails
            }
        }

        _logger.LogInformation(
            "Weekly league sync complete. Succeeded: {Succeeded}, Failed: {Failed}",
            succeeded, failed);
    }
}

// FF.Infrastructure/Jobs/PlayerSyncJob.cs

using FF.Application.Players.Commands.SyncPlayers;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

public class PlayerSyncJob(IMediator mediator, ILogger<PlayerSyncJob> logger)
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger<PlayerSyncJob> _logger = logger;

    /// <summary>
    /// Syncs the full Sleeper player universe weekly.
    /// Scheduled for Tuesday 6am — after Monday Night Football finalizes
    /// and Sleeper has updated injury/status data.
    ///
    /// Register in Program.cs after app.Build():
    ///   recurringJobManager.AddOrUpdate{PlayerSyncJob}(
    ///       "player-sync-weekly",
    ///       job => job.SyncPlayersAsync(),
    ///       "0 6 * * 2");  // 6am every Tuesday
    /// </summary>
    [AutomaticRetry(Attempts = 3)]
    public async Task SyncPlayersAsync()
    {
        _logger.LogInformation("Hangfire: Starting weekly player sync job");

        var result = await _mediator.Send(new SyncPlayersCommand());

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Hangfire: Player sync complete — " +
                "Added: {Added}, Updated: {Updated}, Skipped: {Skipped}, " +
                "Duration: {Duration:0.0}s",
                result.Value!.PlayersAdded,
                result.Value.PlayersUpdated,
                result.Value.PlayersSkipped,
                result.Value.Duration.TotalSeconds);
        }
        else
        {
            _logger.LogError(
                "Hangfire: Player sync failed — {Error}",
                result.Error?.Message);

            // Throwing causes Hangfire to mark the job as failed
            // and retry according to AutomaticRetry policy
            throw new InvalidOperationException(
                $"Player sync failed: {result.Error?.Message}");
        }
    }
}

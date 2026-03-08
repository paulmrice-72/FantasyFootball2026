// FF.Infrastructure/Jobs/HistoricalStatsSyncJob.cs
//
// Hangfire recurring job for weekly historical stats sync.
// Registered in Program.cs to run every Tuesday at 8:00 AM —
// AFTER PlayerSyncJob (6:00 AM) so the player universe is current.
//
// WEEKLY SYNC WORKFLOW:
//   1. Download updated player_stats_2024.csv from nflfastR releases
//      https://github.com/nflverse/nflverse-data/releases/tag/player_stats
//   2. Replace the file in Data/Historical/nflfastr/player_stats_2024.csv
//   3. This job runs Tuesday and upserts new/updated documents to MongoDB
//
// WHY CURRENT SEASON ONLY:
//   2022 and 2023 are complete seasons — their data never changes.
//   Only 2024 gets new rows each week during the active season.
//   Use POST /api/v1/stats/import to re-import all seasons if needed.
//
// RETRY POLICY:
//   2 retries with 5-minute and 10-minute delays.
//   Throws on failure to trigger Hangfire retry mechanism.

using Hangfire;
using MediatR;
using FF.Application.Stats.Commands;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using FF.Application.Stats.Commands.ImportHistoricalStats;

namespace FF.Infrastructure.Jobs;

public class HistoricalStatsSyncJob(
    IMediator mediator,
    IConfiguration configuration,
    ILogger<HistoricalStatsSyncJob> logger)
{
    private readonly IMediator _mediator = mediator;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<HistoricalStatsSyncJob> _logger = logger;

    [AutomaticRetry(Attempts = 2, DelaysInSeconds = new[] { 300, 600 })]
    public async Task SyncCurrentSeasonAsync()
    {
        _logger.LogInformation("HistoricalStatsSyncJob started — syncing current season 2024");

        // Weekly sync: current season only, skip PFR validation for speed
        var command = new ImportHistoricalStatsCommand(
            Seasons: [2024],
            RunPfrValidation: false
        );

        var result = await _mediator.Send(command);

        if (result.IsFailure)
        {
            _logger.LogError(
                "HistoricalStatsSyncJob failed: {Error}",
                result.Error.Message);

            // Throw to trigger Hangfire retry
            throw new Exception($"Historical stats sync failed: {result.Error.Message}");
        }

        _logger.LogInformation(
            "HistoricalStatsSyncJob complete: {Inserted} inserted, {Replaced} replaced",
            result.Value.TotalInserted,
            result.Value.TotalReplaced);
    }
}
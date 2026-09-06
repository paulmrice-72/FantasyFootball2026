// FF.Infrastructure/Jobs/UsageMetricsAggregationJob.cs
using FF.Application.Interfaces.Services;
using FF.Application.Interfaces.Services.Usage;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

public class UsageMetricsAggregationJob(
    IUsageMetricsService usageMetricsService,
    INflContextService nflContext,
    ILogger<UsageMetricsAggregationJob> logger)
{
    // Called by Hangfire recurring registration — reads season from admin settings
    public async Task ExecuteAsync()
    {
        var season = await nflContext.GetSeasonAsync();
        await ExecuteAsync(season);
    }

    // Called by admin job trigger with explicit season (POST api/v1/admin/jobs/run-usage-metrics)
    public async Task<int> ExecuteAsync(int season)
    {
        logger.LogInformation("UsageMetricsAggregationJob started for season {Season}", season);

        var processed = await usageMetricsService.AggregateAllPlayersAsync(season);

        if (processed == 0)
        {
            logger.LogWarning(
                "UsageMetricsAggregationJob completed for season {Season} but processed 0 players "
                + "— no rows were written.",
                season);
        }
        else
        {
            logger.LogInformation(
                "UsageMetricsAggregationJob completed for season {Season} — {Count} players processed",
                season, processed);
        }

        return processed;
    }
}

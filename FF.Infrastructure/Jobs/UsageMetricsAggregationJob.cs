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

    // Called by admin job trigger with explicit season
    public async Task ExecuteAsync(int season)
    {
        logger.LogInformation("UsageMetricsAggregationJob started for season {Season}", season);
        await usageMetricsService.AggregateAllPlayersAsync(season);
        logger.LogInformation("UsageMetricsAggregationJob completed for season {Season}", season);
    }
}
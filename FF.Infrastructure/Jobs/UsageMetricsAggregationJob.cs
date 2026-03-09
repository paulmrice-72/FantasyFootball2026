// FF.Infrastructure/Jobs/UsageMetricsAggregationJob.cs
using FF.Application.Interfaces.Services.Usage;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

public class UsageMetricsAggregationJob(
    IUsageMetricsService usageMetricsService,
    ILogger<UsageMetricsAggregationJob> logger)
{
    private readonly IUsageMetricsService _usageMetricsService = usageMetricsService;
    private readonly ILogger<UsageMetricsAggregationJob> _logger = logger;

    public async Task ExecuteAsync(int season)
    {
        _logger.LogInformation(
            "UsageMetricsAggregationJob started for season {Season}", season);

        await _usageMetricsService.AggregateAllPlayersAsync(season);

        _logger.LogInformation(
            "UsageMetricsAggregationJob completed for season {Season}", season);
    }
}
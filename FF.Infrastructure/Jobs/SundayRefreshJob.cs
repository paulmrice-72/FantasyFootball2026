// FF.Infrastructure/Jobs/SundayRefreshJob.cs
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

/// <summary>
/// Sunday main slate refresh — fires Sunday 11am UTC (6am ET).
/// Recalculates projections and simulation before 1pm ET kickoffs.
/// </summary>
public class SundayRefreshJob(
    ProjectionRefreshJob refreshJob,
    ILogger<SundayRefreshJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        logger.LogInformation("SundayRefreshJob triggered");
        await refreshJob.RunAsync("Sunday", ct);
    }
}
// FF.Infrastructure/Jobs/TnfRefreshJob.cs
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

/// <summary>
/// Thursday Night Football refresh — fires Thursday 6pm UTC (1pm ET).
/// Recalculates projections and simulation before TNF kickoff.
/// </summary>
public class TnfRefreshJob(
    ProjectionRefreshJob refreshJob,
    ILogger<TnfRefreshJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        logger.LogInformation("TnfRefreshJob triggered");
        await refreshJob.RunAsync("TNF", ct);
    }
}
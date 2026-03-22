// FF.Infrastructure/Jobs/MnfRefreshJob.cs
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

/// <summary>
/// Monday Night Football refresh — fires Monday 11pm UTC (6pm ET).
/// Recalculates projections and simulation before MNF kickoff.
/// </summary>
public class MnfRefreshJob(
    ProjectionRefreshJob refreshJob,
    ILogger<MnfRefreshJob> logger)
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        logger.LogInformation("MnfRefreshJob triggered");
        await refreshJob.RunAsync("MNF", ct);
    }
}
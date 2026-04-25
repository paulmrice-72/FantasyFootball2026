// FF.Infrastructure/Jobs/SyncDepthChartsJob.cs
using FF.Application.Features.DepthChart.Commands;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

/// <summary>
/// Downloads nflverse depth_charts_{season}.csv and upserts to MongoDB depth_charts collection.
/// Resolves SleeperPlayerId from GsisId so PlayerCard depth chart tab shows real data.
/// Schedule: Wednesday 8:00 UTC — after RecalculateDynastyValuationsJob (7am).
/// </summary>
public class SyncDepthChartsJob(
    IMediator mediator,
    ILogger<SyncDepthChartsJob> logger)
{
    private readonly IMediator _mediator = mediator;
    private readonly ILogger<SyncDepthChartsJob> _logger = logger;

    [AutomaticRetry(Attempts = 3)]
    public async Task RunAsync(int season, CancellationToken ct = default)
    {
        _logger.LogInformation("SyncDepthChartsJob starting — Season {Season}", season);

        var result = await _mediator.Send(new SyncDepthChartsCommand(season), ct);

        _logger.LogInformation(
            "SyncDepthChartsJob complete — Synced: {Synced}, Elapsed: {Elapsed:F1}s",
            result.Synced, result.Elapsed.TotalSeconds);
    }
}
// FF.Application/Features/DepthChart/Commands/SyncDepthChartsCommandHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FF.Application.Features.DepthChart.Commands;

public class SyncDepthChartsCommandHandler(
    IDepthChartRepository depthChartRepository,
    IPlayerRepository playerRepository,
    INflverseDownloadService nflverseDownload,
    ILogger<SyncDepthChartsCommandHandler> logger)
    : IRequestHandler<SyncDepthChartsCommand, SyncDepthChartsResult>
{
    public async Task<SyncDepthChartsResult> Handle(
        SyncDepthChartsCommand request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("SyncDepthCharts starting — Season {Season}", request.Season);

        var rows = await nflverseDownload.DownloadDepthChartsAsync(request.Season, cancellationToken);

        if (rows.Count == 0)
        {
            logger.LogWarning(
                "No depth chart rows returned — nflverse may not have published {Season} data yet",
                request.Season);
            return new SyncDepthChartsResult(0, 0, sw.Elapsed);
        }

        // Resolve SleeperPlayerId from GsisId so PlayerCard queries can find rows
        var allPlayers = await playerRepository.GetAllAsync(cancellationToken);
        var gsisToSleeper = allPlayers
           .Where(p => !string.IsNullOrEmpty(p.GsisId) && !string.IsNullOrEmpty(p.SleeperPlayerId))
           .GroupBy(p => p.GsisId!)
           .ToDictionary(g => g.Key, g => g.First().SleeperPlayerId!);

        var enriched = rows.Select(r =>
        {
            if (gsisToSleeper.TryGetValue(r.GsisId, out var sid))
                r.SleeperPlayerId = sid;
            return r;
        }).ToList();

        // Upsert all rows to depth_charts collection (sequential, avoids WaitQueueFull)
        await depthChartRepository.UpsertBatchAsync(enriched, cancellationToken);

        sw.Stop();
        logger.LogInformation(
            "SyncDepthCharts complete — {Synced} rows synced in {Elapsed:F1}s",
            enriched.Count, sw.Elapsed.TotalSeconds);

        return new SyncDepthChartsResult(enriched.Count, 0, sw.Elapsed);
    }
}
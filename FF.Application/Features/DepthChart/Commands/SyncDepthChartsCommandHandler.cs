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

        var rows = await nflverseDownload.DownloadDepthChartsAsync(
            request.Season, cancellationToken);

        if (rows.Count == 0)
        {
            logger.LogWarning(
                "No depth chart rows returned — nflverse may not have published {Season} data yet",
                request.Season);
            return new SyncDepthChartsResult(0, 0, sw.Elapsed);
        }

        // Build two lookups for SleeperPlayerId resolution:
        //   1. GsisId → SleeperPlayerId  (preferred — exact match)
        //   2. NormalizedName → SleeperPlayerId  (fallback — for players where Sleeper
        //      didn't return a GSIS ID during sync, e.g. Michael Mayer SleeperPlayerId=9482)
        var allPlayers = await playerRepository.GetAllAsync(cancellationToken);

        var gsisToSleeper = allPlayers
            .Where(p => !string.IsNullOrEmpty(p.GsisId) &&
                        !string.IsNullOrEmpty(p.SleeperPlayerId))
            .GroupBy(p => p.GsisId!)
            .ToDictionary(g => g.Key, g => g.First().SleeperPlayerId!);

        var nameToSleeper = allPlayers
            .Where(p => !string.IsNullOrEmpty(p.SleeperPlayerId) &&
                        !string.IsNullOrEmpty(p.FullName))
            .GroupBy(p => NormalizeName(p.FullName!))
            .ToDictionary(g => g.Key, g => g.First().SleeperPlayerId!);

        var gsisMatched = 0;
        var nameMatched = 0;
        var unmatched = 0;

        var enriched = rows.Select(r =>
        {
            // 1. Try GSIS match
            if (!string.IsNullOrEmpty(r.GsisId) &&
                gsisToSleeper.TryGetValue(r.GsisId, out var sidByGsis))
            {
                r.SleeperPlayerId = sidByGsis;
                gsisMatched++;
                return r;
            }

            // 2. Fall back to name match
            var normalizedName = NormalizeName(r.FullName);
            if (!string.IsNullOrEmpty(normalizedName) &&
                nameToSleeper.TryGetValue(normalizedName, out var sidByName))
            {
                r.SleeperPlayerId = sidByName;
                nameMatched++;
                return r;
            }

            unmatched++;
            return r;
        }).ToList();

        await depthChartRepository.UpsertBatchAsync(enriched, cancellationToken);

        sw.Stop();
        logger.LogInformation(
            "SyncDepthCharts complete — {Total} rows, {GsisMatched} GSIS matched, " +
            "{NameMatched} name matched, {Unmatched} unmatched in {Elapsed:F1}s",
            enriched.Count, gsisMatched, nameMatched, unmatched, sw.Elapsed.TotalSeconds);

        return new SyncDepthChartsResult(enriched.Count, unmatched, sw.Elapsed);
    }

    private static string NormalizeName(string name) =>
        name.ToLowerInvariant()
            .Replace("jr.", "").Replace("sr.", "").Replace("iii", "")
            .Replace("ii", "").Replace("iv", "").Replace("'", "")
            .Replace("-", " ").Replace(".", "")
            .Trim()
            .Replace("  ", " ");
}
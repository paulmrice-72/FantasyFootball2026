// FF.Application/Features/Schedule/Commands/SyncNflScheduleCommandHandler.cs
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FF.Application.Features.Schedule.Commands;

public class SyncNflScheduleCommandHandler(
    INflScheduleRepository scheduleRepository,
    INflverseDownloadService nflverseDownload,
    ILogger<SyncNflScheduleCommandHandler> logger)
    : IRequestHandler<SyncNflScheduleCommand, SyncNflScheduleResult>
{
    public async Task<SyncNflScheduleResult> Handle(
        SyncNflScheduleCommand request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("SyncNflSchedule starting — Season {Season}", request.Season);

        var games = await nflverseDownload.DownloadScheduleAsync(
            request.Season, cancellationToken);

        if (games.Count == 0)
        {
            // Leave whatever is already stored alone. A failed download is not
            // evidence that the season has no games, and clearing the collection
            // on a bad fetch would take every opponent label down with it.
            var existing = await scheduleRepository.CountBySeasonAsync(
                request.Season, cancellationToken);

            var message =
                $"No games parsed for {request.Season} — existing {existing} row(s) left untouched.";

            logger.LogError("SyncNflSchedule failed — {Message}", message);

            return new SyncNflScheduleResult(
                request.Season, 0, 0, 0, 0, Succeeded: false, message, sw.Elapsed);
        }

        await scheduleRepository.UpsertBatchAsync(games, cancellationToken);

        var weeks = games.Select(g => g.Week).Distinct().Count();
        var final = games.Count(g => g.IsFinal());
        var withSpread = games.Count(g => g.SpreadLine.HasValue);

        sw.Stop();
        logger.LogInformation(
            "SyncNflSchedule complete — {Count} games, {Weeks} weeks, {Final} final, " +
            "{Spread} with a closing line, in {Elapsed:F1}s",
            games.Count, weeks, final, withSpread, sw.Elapsed.TotalSeconds);

        return new SyncNflScheduleResult(
            request.Season,
            games.Count,
            weeks,
            final,
            withSpread,
            Succeeded: true,
            Message: null,
            sw.Elapsed);
    }
}

// FF.Infrastructure/Jobs/NflverseDraftPickSyncJob.cs
using FF.Application.Features.DraftTools.Commands.SyncNflverseDraftPicks;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

/// <summary>
/// Downloads nflverse draft picks CSV and populates DraftRound/DraftPick/CollegeTeam
/// on the Players table. Runs daily April 25–May 15 until data is confirmed.
/// Safe to run multiple times — UpdateDraftCapital is idempotent.
/// </summary>
public class NflverseDraftPickSyncJob(
    IMediator mediator,
    ILogger<NflverseDraftPickSyncJob> logger)
{
    [AutomaticRetry(Attempts = 3)]
    public async Task RunAsync(int season, CancellationToken ct = default)
    {
        logger.LogInformation(
            "NflverseDraftPickSyncJob starting for season {Season}", season);

        var result = await mediator.Send(
            new SyncNflverseDraftPicksCommand(season), ct);

        if (result.IsSuccess)
        {
            logger.LogInformation(
                "NflverseDraftPickSyncJob complete — " +
                "Matched: {Matched}, Unmatched: {Unmatched}, Total: {Total}",
                result.Value!.Matched,
                result.Value.Unmatched,
                result.Value.Total);
        }
        else
        {
            logger.LogError(
                "NflverseDraftPickSyncJob failed — {Error}",
                result.Error?.Message);
            throw new InvalidOperationException(
                $"NflverseDraftPickSync failed: {result.Error?.Message}");
        }
    }
}
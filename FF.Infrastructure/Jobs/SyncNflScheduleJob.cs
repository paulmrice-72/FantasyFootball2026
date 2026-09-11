// FF.Infrastructure/Jobs/SyncNflScheduleJob.cs
using FF.Application.Features.Schedule.Commands;
using FF.Application.Interfaces.Services;
using Hangfire;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

/// <summary>
/// Refreshes the NFL schedule from nflverse (FAN-178).
///
/// <para>
/// Runs daily rather than weekly on purpose: the same file carries closing
/// spreads and final scores, so a daily pass keeps completed games current
/// through the week without a second feed. The season is resolved from
/// <see cref="INflContextService"/> rather than passed in, so the recurring
/// registration does not bake a year into a cron entry.
/// </para>
///
/// <para>
/// Scheduled early (06:00 UTC) because everything downstream reads it: the week
/// resolution in <c>NflContextService</c>, the opponent on every projection, and
/// the game-script spread.
/// </para>
/// </summary>
public class SyncNflScheduleJob(
    IMediator mediator,
    INflContextService nflContext,
    ILogger<SyncNflScheduleJob> logger)
{
    // Deliberately not the default 3. A failed schedule fetch is almost always
    // nflverse being briefly unreachable; one retry covers that, and the daily
    // cadence covers the rest. Retrying hard against an upstream that is down
    // is the amplification pattern FAN-177 was raised for.
    [AutomaticRetry(Attempts = 1)]
    public async Task RunAsync(CancellationToken ct = default)
    {
        var season = await nflContext.GetSeasonAsync();

        logger.LogInformation("SyncNflScheduleJob starting — Season {Season}", season);

        var result = await mediator.Send(new SyncNflScheduleCommand(season), ct);

        if (!result.Succeeded)
        {
            logger.LogError(
                "SyncNflScheduleJob did not import a schedule for {Season}: {Message}",
                season, result.Message);
            return;
        }

        logger.LogInformation(
            "SyncNflScheduleJob complete — {Games} games across {Weeks} weeks, " +
            "{Final} final, {Spread} with a closing line ({Elapsed:F1}s)",
            result.GamesImported, result.WeeksCovered, result.GamesFinal,
            result.GamesWithSpread, result.Elapsed.TotalSeconds);
    }
}

// FF.Infrastructure/Jobs/ProjectionRefreshJob.cs
using FF.Application.Features.Projections.Commands.CalculateProjections;
using FF.Application.Features.Simulations.Commands.RunSimulations;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

/// <summary>
/// Shared pipeline: recalculate projections then re-run Monte Carlo simulation
/// for the current NFL week. Used by all game-day refresh jobs.
/// </summary>
public class ProjectionRefreshJob(
    IMediator mediator,
    ILogger<ProjectionRefreshJob> logger)
{
    public async Task RunAsync(string triggerLabel, CancellationToken ct = default)
    {
        var season = GetCurrentNflSeason();
        var week = GetCurrentNflWeek();

        logger.LogInformation(
            "ProjectionRefreshJob [{Trigger}] starting — Season {Season} Week {Week}",
            triggerLabel, season, week);

        // Step 1 — recalculate projections with latest snap/usage/matchup data
        var projResult = await mediator.Send(
            new CalculateProjectionsCommand(season, week), ct);

        if (!projResult.IsSuccess)
        {
            logger.LogError(
                "ProjectionRefreshJob [{Trigger}] — projection calculation failed: {Error}",
                triggerLabel, projResult.Error);
            return;
        }

        logger.LogInformation(
            "ProjectionRefreshJob [{Trigger}] — {Calculated} projections calculated, {Skipped} skipped",
            triggerLabel, projResult.Value.ProjectionsCalculated, projResult.Value.PlayersSkipped);

        // Step 2 — re-run Monte Carlo simulation on fresh projections
        var simResult = await mediator.Send(
            new RunSimulationsCommand(season, week), ct);

        if (!simResult.IsSuccess)
        {
            logger.LogError(
                "ProjectionRefreshJob [{Trigger}] — simulation failed: {Error}",
                triggerLabel, simResult.Error);
            return;
        }

        logger.LogInformation(
            "ProjectionRefreshJob [{Trigger}] complete — {Simulated} simulated, {Skipped} skipped in {Elapsed}ms",
            triggerLabel,
            simResult.Value.Simulated,
            simResult.Value.Skipped,
            simResult.Value.Elapsed.TotalMilliseconds);
    }

    private static int GetCurrentNflSeason()
    {
        var now = DateTime.UtcNow;
        return now.Month >= 3 ? now.Year : now.Year - 1;
    }

    private static int GetCurrentNflWeek()
    {
        var now = DateTime.UtcNow;
        var season = GetCurrentNflSeason();
        var sept1 = new DateTime(season, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var daysUntilThursday = ((int)DayOfWeek.Thursday - (int)sept1.DayOfWeek + 7) % 7;
        var seasonStart = sept1.AddDays(daysUntilThursday);
        if (now < seasonStart) return 18;
        var week = (int)((now - seasonStart).TotalDays / 7) + 1;
        return Math.Clamp(week, 1, 18);
    }
}
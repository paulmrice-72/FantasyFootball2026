// FF.Infrastructure/Jobs/ProjectionRefreshJob.cs
using FF.Application.Features.Projections.Commands.CalculateProjections;
using FF.Application.Features.Simulations.Commands.RunSimulations;
using FF.Application.Interfaces.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

public class ProjectionRefreshJob(
    IMediator mediator,
    INflContextService nflContext,
    ILogger<ProjectionRefreshJob> logger)
{
    public async Task RunAsync(string triggerLabel, CancellationToken ct = default)
    {
        var (season, week) = await nflContext.GetContextAsync();

        logger.LogInformation(
            "ProjectionRefreshJob [{Trigger}] starting — Season {Season} Week {Week}",
            triggerLabel, season, week);

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
            triggerLabel, simResult.Value.Simulated, simResult.Value.Skipped,
            simResult.Value.Elapsed.TotalMilliseconds);
    }
}
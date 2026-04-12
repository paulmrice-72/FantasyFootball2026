// FF.Infrastructure/Jobs/SimulationJob.cs
using FF.Application.Common;
using FF.Application.Features.Simulations.Commands.RunSimulations;
using FF.Application.Interfaces.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

public class SimulationJob(
    IMediator mediator,
    ICacheService cache,
    INflContextService nflContext,
    ILogger<SimulationJob> logger)
{
    public async Task RunAsync()
    {
        var (season, week) = await nflContext.GetContextAsync();

        logger.LogInformation(
            "SimulationJob starting for Season {Season} Week {Week}", season, week);

        var result = await mediator.Send(new RunSimulationsCommand(season, week));

        if (result.IsSuccess)
        {
            logger.LogInformation(
                "SimulationJob complete — {Simulated} simulated, {Skipped} skipped in {Elapsed}ms",
                result.Value.Simulated, result.Value.Skipped,
                result.Value.Elapsed.TotalMilliseconds);

            cache.Remove(CacheKeys.Projections(season, week));
            logger.LogInformation(
                "SimulationJob — projection cache invalidated for {Season} Week {Week}",
                season, week);
        }
        else
        {
            logger.LogError("SimulationJob failed: {Error}", result.Error);
        }
    }
}
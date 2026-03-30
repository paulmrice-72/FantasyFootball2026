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
    ILogger<SimulationJob> logger)
{
    public async Task RunAsync()
    {
        var season = GetCurrentNflSeason();
        var week = GetCurrentNflWeek();

        logger.LogInformation(
            "SimulationJob starting for Season {Season} Week {Week}",
            season, week);

        var result = await mediator.Send(new RunSimulationsCommand(season, week));

        if (result.IsSuccess)
        {
            logger.LogInformation(
                "SimulationJob complete — {Simulated} simulated, {Skipped} skipped in {Elapsed}ms",
                result.Value.Simulated,
                result.Value.Skipped,
                result.Value.Elapsed.TotalMilliseconds);

            // Bust projection cache — next VORP query will re-hydrate from MongoDB
            cache.Remove(CacheKeys.Projections(season, week));
            logger.LogInformation(
                "SimulationJob — projection cache invalidated for {Season} Week {Week}",
                season, week);
        }
        else
        {
            logger.LogError(
                "SimulationJob failed: {Error}", result.Error);
        }
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
        var seasonStart = GetSeasonStart(season);

        if (now < seasonStart) return 18;

        var daysSinceStart = (now - seasonStart).TotalDays;
        var week = (int)(daysSinceStart / 7) + 1;
        return Math.Clamp(week, 1, 18);
    }

    private static DateTime GetSeasonStart(int season)
    {
        var sept1 = new DateTime(season, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var daysUntilThursday = ((int)DayOfWeek.Thursday - (int)sept1.DayOfWeek + 7) % 7;
        return sept1.AddDays(daysUntilThursday);
    }
}
// FF.Infrastructure/Jobs/SimulationJob.cs
using FF.Application.Features.Simulations.Commands.RunSimulations;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

public class SimulationJob(
    IMediator mediator,
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
        // NFL regular season starts first Thursday of September
        // Returns 1-18 during season, 18 in off-season (safe default for backfill)
        var now = DateTime.UtcNow;
        var season = GetCurrentNflSeason();
        var seasonStart = GetSeasonStart(season);

        if (now < seasonStart) return 18; // off-season — use last week as default

        var daysSinceStart = (now - seasonStart).TotalDays;
        var week = (int)(daysSinceStart / 7) + 1;
        return Math.Clamp(week, 1, 18);
    }

    private static DateTime GetSeasonStart(int season)
    {
        // First Thursday of September for the given season year
        var sept1 = new DateTime(season, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var daysUntilThursday = ((int)DayOfWeek.Thursday - (int)sept1.DayOfWeek + 7) % 7;
        return sept1.AddDays(daysUntilThursday);
    }
}
// FF.Infrastructure/Jobs/VorpCalculationJob.cs
using FF.Application.Features.Vorp.Commands.CalculateVorp;
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Jobs;

/// <summary>
/// Recomputes the VORP board for every active league. FAN-118.
///
/// <para>
/// Runs per league rather than once globally, because replacement level is a property
/// of a league, not of the player pool: a superflex league starts roughly twice the
/// quarterbacks, and the free-agent baseline depends entirely on who that league's
/// members have rostered.
/// </para>
///
/// <para>
/// Should be scheduled AFTER the simulation and projection jobs — it reads what they
/// produce. One league failing does not stop the rest; a bad roster import in one
/// league should not silently cost every other league its waiver board.
/// </para>
/// </summary>
public class VorpCalculationJob(
    IMediator mediator,
    ILeagueRepository leagueRepository,
    INflContextService nflContext,
    ILogger<VorpCalculationJob> logger)
{
    public async Task RunAsync()
    {
        var (season, week) = await nflContext.GetContextAsync();
        var leagues = await leagueRepository.GetActiveLeaguesAsync(CancellationToken.None);

        if (leagues.Count == 0)
        {
            logger.LogInformation("VorpCalculationJob — no active leagues; nothing to compute.");
            return;
        }

        logger.LogInformation(
            "VorpCalculationJob starting for Season {Season} Week {Week} across {Count} leagues",
            season, week, leagues.Count);

        var succeeded = 0;
        var failed = 0;

        foreach (var league in leagues)
        {
            if (string.IsNullOrWhiteSpace(league.SleeperLeagueId)) continue;

            try
            {
                var result = await mediator.Send(
                    new CalculateVorpCommand(league.SleeperLeagueId, season, week));

                succeeded++;

                logger.LogInformation(
                    "VORP for league {League}: {Players} players, {Teams} teams, " +
                    "{FreeAgents} free agents",
                    league.SleeperLeagueId, result.PlayersScored, result.TeamCount,
                    result.FreeAgents);

                if (result.Warning is not null)
                    logger.LogWarning(
                        "VORP for league {League}: {Warning}",
                        league.SleeperLeagueId, result.Warning);

                if (result.LegacyPointsOnlyFallbacks > 0)
                    logger.LogWarning(
                        "VORP for league {League}: {Count} projections had no stat line and fell " +
                        "back to the cached half-PPR column — those are NOT scored in this " +
                        "league's format",
                        league.SleeperLeagueId, result.LegacyPointsOnlyFallbacks);
            }
            catch (Exception ex)
            {
                failed++;
                logger.LogError(ex,
                    "VorpCalculationJob failed for league {League} — continuing with the rest",
                    league.SleeperLeagueId);
            }
        }

        logger.LogInformation(
            "VorpCalculationJob complete — {Succeeded} succeeded, {Failed} failed",
            succeeded, failed);
    }
}

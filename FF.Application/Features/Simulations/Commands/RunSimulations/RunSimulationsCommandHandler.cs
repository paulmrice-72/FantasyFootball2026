// FF.Application/Features/Simulations/Commands/RunSimulations/RunSimulationsCommandHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Services;
using FF.Domain.Enums;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FF.Application.Features.Simulations.Commands.RunSimulations;

public class RunSimulationsCommandHandler(
    IPlayerProjectionRepository projectionRepository,
    IPlayerUsageMetricsRepository usageMetricsRepository,
    ISimulationResultRepository simulationRepository,
    IVegasLineRepository vegasLineRepository,
    ILogger<RunSimulationsCommandHandler> logger)
    : IRequestHandler<RunSimulationsCommand, Result<RunSimulationsResult>>
{
    private static readonly string[] SupportedPositions = ["QB", "RB", "WR", "TE"];

    public async Task<Result<RunSimulationsResult>> Handle(
        RunSimulationsCommand request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        var simulated = 0;
        var skipped = 0;

        logger.LogInformation(
            "Starting Monte Carlo simulation for Season {Season} Week {Week}",
            request.Season, request.Week);

        var projections = await projectionRepository.GetByWeekAsync(
            request.Season, request.Week, cancellationToken);

        logger.LogInformation(
            "Found {Count} projections to simulate for {Season} Week {Week}",
            projections.Count, request.Season, request.Week);

        // Pre-load all vegas lines for the week — one batch fetch, not per-player
        var vegasLines = await vegasLineRepository.GetByWeekAsync(
            request.Season, request.Week, cancellationToken);

        var vegasLookup = vegasLines
                    .SelectMany(v =>
                    {
                        var homeScript = GameScriptClassifier.Classify(v.HomeSpread).Script.ToString();
                        var awayScript = GameScriptClassifier.Classify(v.AwaySpread).Script.ToString();
                        return new[]
                        {
                    (Team: v.HomeTeam, Spread: v.HomeSpread, GameScript: homeScript),
                    (Team: v.AwayTeam, Spread: v.AwaySpread, GameScript: awayScript)
                        };
                    })
                    .GroupBy(x => x.Team, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First(),
                        StringComparer.OrdinalIgnoreCase);

        logger.LogInformation(
            "Loaded {Count} Vegas lines for Season {Season} Week {Week}",
            vegasLines.Count, request.Season, request.Week);

        var countByPosition = new Dictionary<string, int>
        {
            ["QB"] = 0,
            ["RB"] = 0,
            ["WR"] = 0,
            ["TE"] = 0
        };

        foreach (var projection in projections)
        {
            try
            {
                if (!SupportedPositions.Contains(projection.Position))
                {
                    skipped++;
                    continue;
                }

                if (projection.ProjectedPointsHalfPpr <= 0)
                {
                    skipped++;
                    logger.LogDebug(
                        "Skipped {PlayerId} — zero base projection", projection.PlayerId);
                    continue;
                }

                var usageMetrics = await usageMetricsRepository.GetByPlayerIdAsync(
                    projection.PlayerId, projection.Season, cancellationToken);

                var role = usageMetrics?.Role ?? PlayerRole.Unknown;

                var result = MonteCarloSimulationService.Simulate(projection, role);

                // ── Stamp SleeperPlayerId from projection ─────────────────
                result.SleeperPlayerId = projection.SleeperPlayerId;

                // Stamp Vegas context if available — falls back to defaults on doc
                if (vegasLookup.TryGetValue(projection.NflTeam, out var line))
                {
                    result.Spread = line.Spread;
                    result.GameScript = line.GameScript;
                }

                await simulationRepository.UpsertAsync(result, cancellationToken);

                simulated++;
                countByPosition[projection.Position] =
                    countByPosition.GetValueOrDefault(projection.Position) + 1;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to simulate player {PlayerId}", projection.PlayerId);
                skipped++;
            }
        }

        sw.Stop();

        logger.LogInformation(
            "Monte Carlo complete — {Simulated} simulated, {Skipped} skipped in {Elapsed}ms. " +
            "QB:{QB} RB:{RB} WR:{WR} TE:{TE}",
            simulated, skipped, sw.ElapsedMilliseconds,
            countByPosition["QB"], countByPosition["RB"],
            countByPosition["WR"], countByPosition["TE"]);

        return Result.Success(new RunSimulationsResult(
            simulated, skipped, request.Season, request.Week, sw.Elapsed));
    }
}
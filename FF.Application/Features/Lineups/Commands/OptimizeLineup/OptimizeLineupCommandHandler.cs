// FF.Application/Features/Lineups/Commands/OptimizeLineup/OptimizeLineupCommandHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Services.LineupOptimizer;
using FF.Domain.ValueObjects;
using FF.SharedKernel.Common;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FF.Application.Features.Lineups.Commands.OptimizeLineup;

public class OptimizeLineupCommandHandler(
    ISimulationResultRepository simulationRepository,
    ILogger<OptimizeLineupCommandHandler> logger)
    : IRequestHandler<OptimizeLineupCommand, Result<LineupOptimizerResult>>
{
    public async Task<Result<LineupOptimizerResult>> Handle(
        OptimizeLineupCommand request,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Optimizing lineup for Season {Season} Week {Week} Mode {Mode}",
            request.Season, request.Week, request.Mode);

        // Load simulation results — floor/median/ceiling per player
        var simResults = await simulationRepository.GetByWeekAsync(
            request.Season, request.Week, cancellationToken);

        if (simResults.Count == 0)
        {
            logger.LogWarning(
                "No simulation results found for Season {Season} Week {Week}",
                request.Season, request.Week);
            return Result.Failure<LineupOptimizerResult>(
                new Error("Optimizer.NoData",
                    $"No simulation results found for {request.Season} Week {request.Week}. " +
                    "Run simulations first via POST /api/v1/projections/simulate."));
        }

        var lockedIds = request.LockedPlayerIds ?? [];
        var excludedIds = request.ExcludedPlayerIds ?? [];

        // Map simulation results to optimizer input slots
        var players = simResults
            .Select(s => new PlayerSlot
            {
                PlayerId = s.PlayerId,
                PlayerName = s.PlayerName,
                Position = s.Position,
                NflTeam = s.NflTeam,
                ProjectedMedian = s.Median,
                ProjectedFloor = s.Floor,
                ProjectedCeiling = s.Ceiling,
                IsLocked = lockedIds.Contains(s.PlayerId),
                IsExcluded = excludedIds.Contains(s.PlayerId)
            })
            .ToList();

        var optimizerInput = new LineupOptimizerInput
        {
            AvailablePlayers = players,
            RosterConfig = RosterConfiguration.Standard,
            Mode = request.Mode,
            LockedPlayerIds = lockedIds,
            ExcludedPlayerIds = excludedIds
        };

        var result = LineupOptimizerService.Optimize(optimizerInput);

        if (!result.Success)
        {
            logger.LogWarning("Optimizer failed: {Error}", result.ErrorMessage);
            return Result.Failure<LineupOptimizerResult>(
                new Error("Optimizer.Failed", result.ErrorMessage ?? "Unknown error"));
        }

        logger.LogInformation(
            "Lineup optimized — {Count} players, {Points} projected points, mode: {Mode}",
            result.Lineup.Count, result.TotalProjectedPoints, result.Mode);

        return Result.Success(result);
    }
}
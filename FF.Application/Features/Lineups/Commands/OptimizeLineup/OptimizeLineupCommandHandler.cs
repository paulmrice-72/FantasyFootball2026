// FF.Application/Features/Lineups/Commands/OptimizeLineup/OptimizeLineupCommandHandler.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Services.LineupOptimizer;
using FF.Domain.Documents;
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
            "Optimizing lineup Season {Season} Week {Week} Mode {Mode} RiskProfile {RiskProfile} RosterFilter {RosterFilter}",
            request.Season, request.Week, request.Mode,
            request.RiskProfile?.ToString() ?? "None",
            request.RosterSleeperIds?.Count.ToString() ?? "None");

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

        // TEAM-002: if roster filter provided, restrict pool to rostered players only
        var rosterIds = request.RosterSleeperIds;
        var filteredResults = rosterIds is { Count: > 0 }
            ? simResults.Where(s => rosterIds.Contains(s.SleeperPlayerId ?? s.PlayerId)).ToList()
            : simResults;

        if (filteredResults.Count == 0)
        {
            logger.LogWarning(
                "No simulation results matched roster SleeperIds for Season {Season} Week {Week}",
                request.Season, request.Week);
            return Result.Failure<LineupOptimizerResult>(
                new Error("Optimizer.NoRosterData",
                    "No simulation data found for your rostered players. " +
                    "Ensure simulations have been run with SleeperPlayerId stamped."));
        }

        var lockedIds = request.LockedPlayerIds ?? [];
        var excludedIds = request.ExcludedPlayerIds ?? [];

        var players = filteredResults
            .Select(s => new PlayerSlot
            {
                PlayerId = s.PlayerId,
                PlayerName = s.PlayerName,
                Position = s.Position,
                NflTeam = s.NflTeam,
                ProjectedMedian = s.Median,
                ProjectedFloor = s.Floor,
                ProjectedCeiling = s.Ceiling,
                BoomProbability = s.BoomProbability,
                BustProbability = s.BustProbability,
                IsLocked = lockedIds.Contains(s.PlayerId),
                IsExcluded = excludedIds.Contains(s.PlayerId)
            })
            .ToList();

        var optimizerInput = new LineupOptimizerInput
        {
            AvailablePlayers = players,
            RosterConfig = RosterConfiguration.Standard,
            Mode = request.Mode,
            RiskProfile = request.RiskProfile,
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
            "Lineup optimized — {Count} players, {Points} pts, Mode: {Mode}, RiskProfile: {Profile}",
            result.Lineup.Count, result.TotalProjectedPoints,
            result.Mode, result.RiskProfile?.ToString() ?? "None");

        return Result.Success(result);
    }
}
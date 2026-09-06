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
    ILeagueRepository leagueRepository,
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
                ProjectedMean = s.Mean > 0m ? s.Mean : s.Median,
                ProjectedFloor = s.Floor,
                ProjectedCeiling = s.Ceiling,
                BoomProbability = s.BoomProbability,
                BustProbability = s.BustProbability,
                IsLocked = lockedIds.Contains(s.PlayerId),
                IsExcluded = excludedIds.Contains(s.PlayerId)
            })
            .ToList();

        // The league's own starting slots, not RosterConfiguration.Standard. Standard
        // is QB/RB/RB/WR/WR/TE/FLEX — a 2-WR lineup — so a 3-WR league was being
        // optimised against the wrong shape and quietly dropped a receiver.
        // Resolution mirrors GetLineupCardQuery: by season first, then the active
        // list, because a league row can exist under a different season key.
        var rosterConfig = await ResolveRosterConfigAsync(
            request.SleeperLeagueId, request.Season, cancellationToken);

        var optimizerInput = new LineupOptimizerInput
        {
            AvailablePlayers = players,
            RosterConfig = rosterConfig,
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

    /// <summary>
    /// The league's starting slots, or <see cref="RosterConfiguration.Standard"/> when
    /// no league was supplied or none could be found. A wrong shape here is silent —
    /// the solver happily returns a valid lineup for the wrong league — so the
    /// fallback is logged rather than taken quietly.
    /// </summary>
    private async Task<RosterConfiguration> ResolveRosterConfigAsync(
        string? sleeperLeagueId, int season, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(sleeperLeagueId))
        {
            logger.LogWarning(
                "No SleeperLeagueId supplied — optimising against RosterConfiguration.Standard. "
                + "Starting slots may not match the league's actual lineup.");
            return RosterConfiguration.Standard;
        }

        var league = await leagueRepository.GetBySleeperIdAsync(sleeperLeagueId, season, ct);

        if (league is null)
        {
            var activeLeagues = await leagueRepository.GetActiveLeaguesAsync(ct);
            league = activeLeagues.FirstOrDefault(l => l.SleeperLeagueId == sleeperLeagueId);
        }

        var config = league?.GetRosterConfiguration();

        if (config is null)
        {
            logger.LogWarning(
                "League {LeagueId} not found for season {Season} — optimising against "
                + "RosterConfiguration.Standard.", sleeperLeagueId, season);
            return RosterConfiguration.Standard;
        }

        logger.LogInformation(
            "Optimising league {LeagueId} with QB {Qb} RB {Rb} WR {Wr} TE {Te} FLEX {Flex}, "
            + "{Total} starters.",
            sleeperLeagueId, config.QbSlots, config.RbSlots, config.WrSlots,
            config.TeSlots, config.FlexSlotDefinitions.Count, config.TotalStarters);

        return config;
    }
}
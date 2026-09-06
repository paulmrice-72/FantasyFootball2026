// FF.Application/Features/Lineups/Commands/OptimizeLineup/OptimizeLineupCommand.cs
using FF.Application.Services.LineupOptimizer;
using FF.Domain.Enums;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.Lineups.Commands.OptimizeLineup;

public record OptimizeLineupCommand(
    int Season,
    int Week,
    // Mean, not Median: the optimiser maximises a SUM, and expectations add.
    // Median remains selectable from the Score Mode dropdown.
    OptimizationMode Mode = OptimizationMode.Mean,
    RiskProfile? RiskProfile = null,
    IReadOnlyList<string>? LockedPlayerIds = null,
    IReadOnlyList<string>? ExcludedPlayerIds = null,
    IReadOnlyList<string>? RosterSleeperIds = null,   // ← TEAM-002: pre-filter to roster
    // Without this the handler falls back to RosterConfiguration.Standard —
    // QB/RB/RB/WR/WR/TE/FLEX — and silently optimises a 2-WR lineup for a
    // 3-WR league. Always pass it when the caller knows which league it is.
    string? SleeperLeagueId = null)
    : IRequest<Result<LineupOptimizerResult>>;
// FF.Application/Features/Lineups/Commands/OptimizeLineup/OptimizeLineupCommand.cs
using FF.Application.Services.LineupOptimizer;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Features.Lineups.Commands.OptimizeLineup;

public record OptimizeLineupCommand(
    int Season,
    int Week,
    OptimizationMode Mode = OptimizationMode.Median,
    IReadOnlyList<string>? LockedPlayerIds = null,
    IReadOnlyList<string>? ExcludedPlayerIds = null)
    : IRequest<Result<LineupOptimizerResult>>;
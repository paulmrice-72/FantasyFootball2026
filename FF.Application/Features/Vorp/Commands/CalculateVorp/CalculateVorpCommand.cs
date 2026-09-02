// FF.Application/Features/Vorp/Commands/CalculateVorp/CalculateVorpCommand.cs
using MediatR;

namespace FF.Application.Features.Vorp.Commands.CalculateVorp;

/// <summary>
/// Recomputes value over replacement for one league and week. FAN-118.
/// League-scoped by necessity: both baselines depend on the league.
/// </summary>
public record CalculateVorpCommand(
    string SleeperLeagueId,
    int Season,
    int Week) : IRequest<CalculateVorpResult>;

public record CalculateVorpResult(
    string SleeperLeagueId,
    int Season,
    int Week,
    int TeamCount,
    int PlayersScored,
    int RosteredPlayers,
    int FreeAgents,
    int LegacyPointsOnlyFallbacks,
    int MissingDistribution,
    IReadOnlyDictionary<string, decimal> StructuralReplacementByPosition,
    IReadOnlyDictionary<string, decimal?> FreeAgentReplacementByPosition,
    IReadOnlyList<string> PositionsWithExhaustedPool,
    string? Warning);

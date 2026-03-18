// FF.Application/Services/LineupOptimizer/LineupOptimizerModels.cs
using FF.Domain.ValueObjects;

namespace FF.Application.Services.LineupOptimizer;

public class LineupOptimizerInput
{
    /// <summary>All available players with their projections and simulation data.</summary>
    public IReadOnlyList<PlayerSlot> AvailablePlayers { get; init; } = [];

    public RosterConfiguration RosterConfig { get; init; } = RosterConfiguration.Standard;
    public OptimizationMode Mode { get; init; } = OptimizationMode.Median;

    /// <summary>Player IDs that must be included in the lineup.</summary>
    public IReadOnlyList<string> LockedPlayerIds { get; init; } = [];

    /// <summary>Player IDs that must be excluded from the lineup.</summary>
    public IReadOnlyList<string> ExcludedPlayerIds { get; init; } = [];
}

public record PlayerSlot
{
    public string PlayerId { get; init; } = string.Empty;
    public string PlayerName { get; init; } = string.Empty;
    public string Position { get; init; } = string.Empty;
    public string NflTeam { get; init; } = string.Empty;
    public decimal ProjectedMedian { get; init; }
    public decimal ProjectedFloor { get; init; }
    public decimal ProjectedCeiling { get; init; }
    public bool IsLocked { get; init; }
    public bool IsExcluded { get; init; }
}

public class LineupOptimizerResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<OptimizedSlot> Lineup { get; init; } = [];
    public decimal TotalProjectedPoints { get; init; }
    public OptimizationMode Mode { get; init; }

    public static LineupOptimizerResult Failed(string error) =>
        new() { Success = false, ErrorMessage = error };
}

public class OptimizedSlot
{
    public string PlayerId { get; init; } = string.Empty;
    public string PlayerName { get; init; } = string.Empty;
    public string Position { get; init; } = string.Empty;
    public string SlotType { get; init; } = string.Empty;  // "QB", "RB", "WR", "TE", "FLEX"
    public decimal ProjectedPoints { get; init; }
}

public enum OptimizationMode
{
    Median,       // maximize median projected points — balanced
    Floor,        // maximize floor — safe/conservative lineup
    Ceiling       // maximize ceiling — boom or bust
}
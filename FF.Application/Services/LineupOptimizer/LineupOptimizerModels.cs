// FF.Application/Services/LineupOptimizer/LineupOptimizerModels.cs
using FF.Domain.Enums;
using FF.Domain.ValueObjects;

namespace FF.Application.Services.LineupOptimizer;

public class LineupOptimizerInput
{
    public IReadOnlyList<PlayerSlot> AvailablePlayers { get; init; } = [];
    public RosterConfiguration RosterConfig { get; init; } = RosterConfiguration.Standard;
    public OptimizationMode Mode { get; init; } = OptimizationMode.Median;
    public IReadOnlyList<string> LockedPlayerIds { get; init; } = [];
    public IReadOnlyList<string> ExcludedPlayerIds { get; init; } = [];
    /// <summary>When set, overrides Mode for score selection in the solver.</summary>
    public RiskProfile? RiskProfile { get; init; }
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

    /// <summary>
    /// Arithmetic mean of the simulated distribution — the player's EXPECTED score.
    ///
    /// This is the number to use whenever scores are going to be ADDED, because
    /// expectations are additive and medians are not: the median of a sum of
    /// right-skewed distributions is not the sum of their medians. The stat-line
    /// simulation is deliberately right-skewed (see MonteCarloSimulationService),
    /// so summing medians understates a lineup total by roughly 6-11% depending on
    /// position — measured against dev 2026 wk0: RB 89.2% of mean, TE 90.1%,
    /// QB 93.7%, WR 94.3%.
    ///
    /// ProjectedMedian remains the right number for a single-player "typical week"
    /// question, which is why both are carried rather than one replacing the other.
    /// </summary>
    public decimal ProjectedMean { get; init; }
    public bool IsLocked { get; init; }
    public bool IsExcluded { get; init; }

    /// <summary>
    /// False when no projection exists for this player — K and DEF today (FAN-124),
    /// plus anyone the engine could not build a stat line for.
    ///
    /// The solver still needs a number to sort on, so ProjectedMedian stays 0m for
    /// these. This flag is what keeps the UI from presenting that 0 as a forecast:
    /// "0.0 projected" and "not projected" are different claims, and only one of
    /// them is true.
    /// </summary>
    public bool HasProjection { get; init; } = true;
    /// <summary>From Monte Carlo output — fraction 0-1.</summary>
    public decimal? BoomProbability { get; init; }
    /// <summary>From Monte Carlo output — fraction 0-1.</summary>
    public decimal? BustProbability { get; init; }
    /// <summary>Projected ownership percentage 0-100. Drives Contrarian penalty.</summary>
    public decimal? OwnershipPct { get; init; }
}

public class LineupOptimizerResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public IReadOnlyList<OptimizedSlot> Lineup { get; init; } = [];
    public decimal TotalProjectedPoints { get; init; }
    public OptimizationMode Mode { get; init; }
    public RiskProfile? RiskProfile { get; init; }

    public static LineupOptimizerResult Failed(string error) =>
        new() { Success = false, ErrorMessage = error };
}

public class OptimizedSlot
{
    public string PlayerId { get; init; } = string.Empty;
    public string PlayerName { get; init; } = string.Empty;
    public string Position { get; init; } = string.Empty;
    public string SlotType { get; init; } = string.Empty;
    public decimal ProjectedPoints { get; init; }
    /// <summary>Risk-adjusted score used by solver. Null when using legacy Mode.</summary>
    public decimal? RiskScore { get; init; }
    /// <summary>Carried through from <see cref="PlayerSlot.HasProjection"/> — see FAN-124.</summary>
    public bool HasProjection { get; init; } = true;
}

public enum OptimizationMode
{
    Median,
    Floor,
    Ceiling,

    /// <summary>
    /// Maximise EXPECTED points. The correct objective for any lineup decision,
    /// because the thing being maximised is a sum. Appended rather than inserted
    /// so the existing ordinals stay stable for anything that persisted them.
    /// </summary>
    Mean
}
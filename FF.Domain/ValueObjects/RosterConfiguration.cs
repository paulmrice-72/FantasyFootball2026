// FF.Domain/ValueObjects/RosterConfiguration.cs
namespace FF.Domain.ValueObjects;

/// <summary>
/// Defines the roster slot requirements for a fantasy league.
/// Used by the lineup optimizer to enforce valid roster construction.
/// </summary>
public class RosterConfiguration
{
    public int QbSlots { get; init; } = 1;
    public int RbSlots { get; init; } = 2;
    public int WrSlots { get; init; } = 2;
    public int TeSlots { get; init; } = 1;
    public int FlexSlots { get; init; } = 1;   // RB/WR/TE eligible
    public int BenchSlots { get; init; } = 6;

    public int TotalStarters => QbSlots + RbSlots + WrSlots + TeSlots + FlexSlots;

    /// <summary>Standard 1QB/2RB/2WR/1TE/1FLEX configuration.</summary>
    public static RosterConfiguration Standard => new();

    /// <summary>Superflex — adds a second QB-eligible FLEX slot.</summary>
    public static RosterConfiguration Superflex => new()
    {
        QbSlots = 1,
        RbSlots = 2,
        WrSlots = 2,
        TeSlots = 1,
        FlexSlots = 2   // second flex is QB/RB/WR/TE eligible
    };
}
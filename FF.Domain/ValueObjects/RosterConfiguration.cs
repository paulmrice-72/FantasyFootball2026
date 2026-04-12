// FF.Domain/ValueObjects/RosterConfiguration.cs
namespace FF.Domain.ValueObjects;

/// <summary>
/// Defines the roster slot requirements for a fantasy league.
/// Used by the lineup optimizer and start/sit engine to enforce
/// valid roster construction including superflex and TE-excluded flex.
/// </summary>
public class RosterConfiguration
{
    public int QbSlots { get; init; } = 1;
    public int RbSlots { get; init; } = 2;
    public int WrSlots { get; init; } = 2;
    public int TeSlots { get; init; } = 1;
    public int BenchSlots { get; init; } = 6;

    /// <summary>
    /// Flex slots and their eligible positions. Each entry is one slot.
    /// Examples:
    ///   RB/WR/TE flex     → ["RB","WR","TE"]
    ///   WR/RB flex (no TE)→ ["RB","WR"]
    ///   Superflex         → ["QB","RB","WR","TE"]
    /// </summary>
    public List<FlexSlotDefinition> FlexSlotDefinitions { get; init; } =
        [
        new FlexSlotDefinition(["RB", "WR", "TE"])
    ];
    public int TotalStarters =>
        QbSlots + RbSlots + WrSlots + TeSlots + FlexSlotDefinitions.Count;

    // ── Factory methods ──────────────────────────────────────────────

    /// <summary>Standard 1QB/2RB/2WR/1TE/1FLEX(RB/WR/TE).</summary>
    public static RosterConfiguration Standard => new();

    /// <summary>
    /// Superflex — 1QB/2RB/2WR/1TE/1FLEX(RB/WR/TE)/1SFLEX(QB/RB/WR/TE).
    /// </summary>
    public static RosterConfiguration Superflex => new()
    {
        QbSlots = 1,
        RbSlots = 2,
        WrSlots = 2,
        TeSlots = 1,
        FlexSlotDefinitions =             // ← renamed
        [
            new FlexSlotDefinition(["RB", "WR", "TE"]),
        new FlexSlotDefinition(["QB", "RB", "WR", "TE"])
        ]
    };

    /// <summary>
    /// Build from Sleeper roster_positions array.
    /// e.g. ["QB","RB","RB","WR","WR","TE","FLEX","SUPER_FLEX","BN","BN"]
    /// </summary>
    public static RosterConfiguration FromSleeperPositions(
        IEnumerable<string> sleeperPositions)
    {
        var positions = sleeperPositions.ToList();

        var qb = positions.Count(p => p == "QB");
        var rb = positions.Count(p => p == "RB");
        var wr = positions.Count(p => p == "WR");
        var te = positions.Count(p => p == "TE");
        var bench = positions.Count(p => p == "BN");

        var flexSlots = new List<FlexSlotDefinition>();

        var flexCount = positions.Count(p => p == "FLEX");
        for (var i = 0; i < flexCount; i++)
            flexSlots.Add(new FlexSlotDefinition(["RB", "WR", "TE"]));

        var sfCount = positions.Count(p => p == "SUPER_FLEX");
        for (var i = 0; i < sfCount; i++)
            flexSlots.Add(new FlexSlotDefinition(["QB", "RB", "WR", "TE"]));

        var wrrbCount = positions.Count(p => p == "WRRB_FLEX");
        for (var i = 0; i < wrrbCount; i++)
            flexSlots.Add(new FlexSlotDefinition(["RB", "WR"]));

        var recCount = positions.Count(p => p == "REC_FLEX");
        for (var i = 0; i < recCount; i++)
            flexSlots.Add(new FlexSlotDefinition(["RB", "WR", "TE"]));

        if (flexSlots.Count == 0)
            flexSlots.Add(new FlexSlotDefinition(["RB", "WR", "TE"]));

        return new RosterConfiguration
        {
            QbSlots = qb,
            RbSlots = rb,
            WrSlots = wr,
            TeSlots = te,
            BenchSlots = bench,
            FlexSlotDefinitions = flexSlots   // ← renamed
        };
    }
}

/// <summary>
/// Defines position eligibility for a single flex slot.
/// </summary>
public record FlexSlotDefinition(List<string> EligiblePositions)
{
    public bool IsEligible(string position) =>
        EligiblePositions.Contains(position, StringComparer.OrdinalIgnoreCase);
}
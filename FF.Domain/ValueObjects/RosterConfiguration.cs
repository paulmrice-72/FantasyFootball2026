// FF.Domain/ValueObjects/RosterConfiguration.cs
namespace FF.Domain.ValueObjects;

/// <summary>
/// Defines the roster slot requirements for a fantasy league.
/// Used by the lineup optimizer, the start/sit engine and the draft board's
/// needs model to enforce valid roster construction including superflex,
/// TE-excluded flex, and the mandatory kicker / defense slots.
/// </summary>
public class RosterConfiguration
{
    public int QbSlots { get; init; } = 1;
    public int RbSlots { get; init; } = 2;
    public int WrSlots { get; init; } = 2;
    public int TeSlots { get; init; } = 1;

    /// <summary>
    /// Kicker slots. 2026-09-07: previously absent from this type entirely, so a
    /// league that requires a K had no way to say so and every consumer treated
    /// a missing kicker as a filled lineup. See DefSlots.
    /// </summary>
    public int KSlots { get; init; } = 0;

    /// <summary>
    /// Team-defense slots (Sleeper "DEF", some feeds "DST" / "D").
    /// </summary>
    public int DefSlots { get; init; } = 0;

    public int BenchSlots { get; init; } = 6;

    /// <summary>
    /// Flex slots and their eligible positions. Each entry is one slot.
    /// Examples:
    ///   RB/WR/TE flex     → ["RB","WR","TE"]
    ///   WR/RB flex (no TE)→ ["RB","WR"]
    ///   WR/TE flex        → ["WR","TE"]
    ///   Superflex         → ["QB","RB","WR","TE"]
    /// </summary>
    public List<FlexSlotDefinition> FlexSlotDefinitions { get; init; } =
    [
        new FlexSlotDefinition(["RB", "WR", "TE"])
    ];

    /// <summary>
    /// Starting slots Sleeper reported that this type does not model — IDP
    /// (DL/LB/DB/IDP_FLEX) and anything new Sleeper introduces.
    ///
    /// This exists so an unrecognised slot is VISIBLE rather than silently
    /// dropped. Dropping one understates TotalStarters, which understates how
    /// many players a roster still needs, which is exactly the class of quiet
    /// wrong answer this file already produced for kickers.
    /// </summary>
    public IReadOnlyList<string> UnsupportedSlots { get; init; } = [];

    /// <summary>
    /// Starting slots the LINEUP OPTIMIZER models: QB, RB, WR, TE and flex.
    ///
    /// Deliberately excludes K, DefSlots and UnsupportedSlots, and must keep
    /// doing so. LineupOptimizerService adds the hard constraint
    ///
    ///     model.Add(LinearExpr.Sum(x) == config.TotalStarters)
    ///
    /// while only ever creating slot variables for the positions above — every
    /// selected player must occupy one. Widening this number without also
    /// teaching the solver to fill a kicker and a defense does not produce a
    /// lineup that is missing two players; it makes the constraint unsatisfiable
    /// and the optimizer returns Failed for every league that plays them.
    ///
    /// Use <see cref="TotalStartingSlots"/> for "what does this league actually
    /// start on Sunday".
    /// </summary>
    public int TotalStarters =>
        QbSlots + RbSlots + WrSlots + TeSlots + FlexSlotDefinitions.Count;

    /// <summary>
    /// Every starting slot the league actually plays — the optimizer's slots plus
    /// the kicker and defense it cannot yet fill, plus anything Sleeper reported
    /// that this type does not model.
    /// </summary>
    public int TotalStartingSlots =>
        TotalStarters + KSlots + DefSlots + UnsupportedSlots.Count;

    // ── Factory methods ──────────────────────────────────────────────

    /// <summary>
    /// Standard 1QB/2RB/2WR/1TE/1FLEX(RB/WR/TE), plus the 1K/1DEF that nearly
    /// every redraft league plays.
    ///
    /// The K and DEF are inert for the lineup optimizer — see
    /// <see cref="TotalStarters"/> — but they matter to the draft board, which
    /// falls back to this when Sleeper has not reported a league's positions and
    /// would otherwise tell a user his roster was complete with two mandatory
    /// slots empty.
    /// </summary>
    public static RosterConfiguration Standard => new()
    {
        KSlots = 1,
        DefSlots = 1
    };

    /// <summary>
    /// Superflex — 1QB/2RB/2WR/1TE/1FLEX(RB/WR/TE)/1SFLEX(QB/RB/WR/TE)/1K/1DEF.
    /// </summary>
    public static RosterConfiguration Superflex => new()
    {
        QbSlots = 1,
        RbSlots = 2,
        WrSlots = 2,
        TeSlots = 1,
        KSlots = 1,
        DefSlots = 1,
        FlexSlotDefinitions =
        [
            new FlexSlotDefinition(["RB", "WR", "TE"]),
            new FlexSlotDefinition(["QB", "RB", "WR", "TE"])
        ]
    };

    /// <summary>
    /// Slot tokens that are roster bookkeeping rather than a starting position.
    /// </summary>
    private static readonly HashSet<string> NonStartingSlots =
        new(StringComparer.OrdinalIgnoreCase) { "BN", "IR", "TAXI" };

    private static readonly HashSet<string> KnownStartingSlots =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "QB", "RB", "WR", "TE", "K", "DEF", "DST", "D",
            "FLEX", "SUPER_FLEX", "WRRB_FLEX", "REC_FLEX"
        };

    /// <summary>
    /// Build from a Sleeper roster_positions array.
    /// e.g. ["QB","RB","RB","WR","WR","TE","FLEX","SUPER_FLEX","K","DEF","BN","BN"]
    /// </summary>
    public static RosterConfiguration FromSleeperPositions(
        IEnumerable<string> sleeperPositions)
    {
        var positions = sleeperPositions
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToList();

        int Count(params string[] tokens) =>
            positions.Count(p => tokens.Contains(p, StringComparer.OrdinalIgnoreCase));

        var flexSlots = new List<FlexSlotDefinition>();

        void AddFlex(int n, params string[] eligible)
        {
            for (var i = 0; i < n; i++)
                flexSlots.Add(new FlexSlotDefinition([.. eligible]));
        }

        AddFlex(Count("FLEX"), "RB", "WR", "TE");
        AddFlex(Count("SUPER_FLEX"), "QB", "RB", "WR", "TE");
        AddFlex(Count("WRRB_FLEX"), "RB", "WR");

        // 2026-09-07 fix: REC_FLEX was building ["RB","WR","TE"] — the same list
        // as a plain FLEX. Sleeper's REC_FLEX is a RECEIVER flex: WR or TE, no
        // running back. Modelling it as RB-eligible credited running backs with
        // a slot they can never occupy, which inflates RB need in exactly the
        // leagues that chose this slot to do the opposite.
        AddFlex(Count("REC_FLEX"), "WR", "TE");

        // 2026-09-07: removed a fabricated fallback flex slot here. The old code
        // ran `if (flexSlots.Count == 0) flexSlots.Add(RB/WR/TE)` — inventing a
        // starting slot for any league that genuinely has none, and thereby
        // reporting one more starter than the league actually plays.
        // A league with no flex now correctly has no flex.

        var unsupported = positions
            .Where(p => !NonStartingSlots.Contains(p) && !KnownStartingSlots.Contains(p))
            .ToList();

        return new RosterConfiguration
        {
            QbSlots = Count("QB"),
            RbSlots = Count("RB"),
            WrSlots = Count("WR"),
            TeSlots = Count("TE"),
            KSlots = Count("K"),
            DefSlots = Count("DEF", "DST", "D"),
            BenchSlots = Count("BN"),
            FlexSlotDefinitions = flexSlots,
            UnsupportedSlots = unsupported
        };
    }

    /// <summary>
    /// Round-trips back to the Sleeper vocabulary, one token per starting slot.
    /// Used to hand a league's real configuration to clients that model slots as
    /// counts (the draft board) without inventing a second vocabulary for them.
    /// </summary>
    public IReadOnlyList<string> ToSleeperPositions()
    {
        var tokens = new List<string>();
        tokens.AddRange(Enumerable.Repeat("QB", QbSlots));
        tokens.AddRange(Enumerable.Repeat("RB", RbSlots));
        tokens.AddRange(Enumerable.Repeat("WR", WrSlots));
        tokens.AddRange(Enumerable.Repeat("TE", TeSlots));

        foreach (var flex in FlexSlotDefinitions)
            tokens.Add(flex.SleeperToken);

        tokens.AddRange(Enumerable.Repeat("K", KSlots));
        tokens.AddRange(Enumerable.Repeat("DEF", DefSlots));
        return tokens;
    }
}

/// <summary>
/// Defines position eligibility for a single flex slot.
/// </summary>
public record FlexSlotDefinition(List<string> EligiblePositions)
{
    public bool IsEligible(string position) =>
        EligiblePositions.Contains(position, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The Sleeper roster_positions token this eligibility set corresponds to.
    /// Unrecognised combinations fall back to FLEX rather than throwing — an
    /// approximate label is better than a crash, and the eligibility list
    /// itself is what the math actually uses.
    /// </summary>
    public string SleeperToken
    {
        get
        {
            var set = new HashSet<string>(EligiblePositions, StringComparer.OrdinalIgnoreCase);
            if (set.SetEquals(new[] { "QB", "RB", "WR", "TE" })) return "SUPER_FLEX";
            if (set.SetEquals(new[] { "RB", "WR" })) return "WRRB_FLEX";
            if (set.SetEquals(new[] { "WR", "TE" })) return "REC_FLEX";
            return "FLEX";
        }
    }
}

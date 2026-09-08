// FF.Application/Services/ReplacementLevelService.cs
using FF.Domain.ValueObjects;

namespace FF.Application.Services;

/// <summary>One player's projection, as L3 sees it. Position is "QB"/"RB"/"WR"/"TE".</summary>
public sealed record ReplacementCandidate(
    string PlayerId,
    string Position,
    decimal ProjectedPoints,
    bool IsRostered);

/// <summary>
/// Both replacement baselines for one position, plus the working that produced them.
/// The intermediate values are kept because "why is this player's VORP 3.2?" is a
/// question the UI has to be able to answer.
/// </summary>
public sealed record PositionReplacement(
    string Position,
    int StartersAbsorbed,
    decimal StructuralLevel,
    bool PoolExhausted,
    decimal? FreeAgentBest,
    decimal? FreeAgentSecondBest,
    string? FreeAgentBestPlayerId);

/// <summary>
/// One position's replacement level resolved from a stored curve — FAN-153.
///
/// <para>
/// <c>StructuralLevel</c> is null only when <c>BeyondStoredDepth</c> is set. The two
/// failure modes are kept apart on purpose:
/// </para>
///
/// <list type="bullet">
/// <item><c>PoolExhausted</c> — the league starts more players at this position than
/// the model projects. Real, if unusual; the weakest projection is the honest
/// baseline and is returned.</item>
/// <item><c>BeyondStoredDepth</c> — the cutoff ran past the stored curve while the
/// pool goes deeper still. Nothing is returned, because the last stored value would
/// read as an exhausted pool and inflate every value above it. The fix is a deeper
/// curve, not a guess.</item>
/// </list>
/// </summary>
public sealed record CurveReplacement(
    string Position,
    int StartersAbsorbed,
    decimal? StructuralLevel,
    bool PoolExhausted,
    bool BeyondStoredDepth);

/// <summary>
/// L3 — league-aware replacement level. FAN-118.
///
/// <para>
/// Pure and static: no repositories, no I/O, no clock. Everything it needs arrives
/// as arguments, which is what makes the numbers reproducible and the thing
/// testable without a database.
/// </para>
///
/// <para><b>Two baselines, because there are two different questions.</b></para>
///
/// <para>
/// <b>Structural</b> — the projection of the first player at a position who would
/// NOT be starting anywhere in the league. Derived from the league's own roster
/// configuration, so a superflex league pulls roughly a full extra round of
/// quarterbacks into "startable" and the QB baseline drops accordingly. This is
/// the classic VORP denominator: stable week to week, comparable across positions,
/// and the right input for rankings, trade value and Top Assets.
/// </para>
///
/// <para>
/// <b>Free agent</b> — the best player at that position nobody in the league
/// rosters. This answers the waiver question instead: not "how good is he" but
/// "how much better is he than what I could pick up for nothing". It moves as
/// rosters move, which is a feature there and a bug in a ranking.
/// </para>
///
/// <para>
/// The free-agent baseline is computed leave-one-out — the best available free
/// agent is scored against the SECOND best, not against himself. Otherwise the top
/// waiver target always scores exactly zero, which is the one player the page most
/// needs to be able to recommend.
/// </para>
/// </summary>
public static class ReplacementLevelService
{
    private static readonly string[] ScoredPositions = ["QB", "RB", "WR", "TE"];

    /// <summary>
    /// Computes both baselines for every scored position.
    /// K and DEF are excluded — they are not projected at all yet (FAN-124), and a
    /// baseline built from an empty pool would be a fabricated zero.
    /// </summary>
    public static IReadOnlyDictionary<string, PositionReplacement> Compute(
        IReadOnlyList<ReplacementCandidate> pool,
        RosterConfiguration config,
        int teamCount)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(config);
        if (teamCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(teamCount), teamCount,
                "Team count must be positive — replacement level is meaningless without a league size.");

        // Descending projection per position. Ties broken by id so the result is
        // deterministic run to run; otherwise two equal projections could swap and
        // quietly change a replacement level.
        var byPosition = ScoredPositions.ToDictionary(
            pos => pos,
            pos => pool
                .Where(c => string.Equals(c.Position, pos, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => c.ProjectedPoints)
                .ThenBy(c => c.PlayerId, StringComparer.Ordinal)
                .ToList());

        var absorbed = AllocateStarters(byPosition, config, teamCount);

        var results = new Dictionary<string, PositionReplacement>(StringComparer.OrdinalIgnoreCase);

        foreach (var pos in ScoredPositions)
        {
            var ranked = byPosition[pos];
            var cutoff = absorbed[pos];

            // The replacement player is the first one past the cutoff. If the pool
            // does not reach that deep the league can start more players at this
            // position than exist projections for — flagged rather than papered over
            // with a zero, because a zero here silently inflates every VORP above it.
            var exhausted = cutoff >= ranked.Count;
            var structural = exhausted
                ? (ranked.Count > 0 ? ranked[^1].ProjectedPoints : 0m)
                : ranked[cutoff].ProjectedPoints;

            var freeAgents = ranked.Where(c => !c.IsRostered).Take(2).ToList();

            results[pos] = new PositionReplacement(
                Position:               pos,
                StartersAbsorbed:       cutoff,
                StructuralLevel:        structural,
                PoolExhausted:          exhausted,
                FreeAgentBest:          freeAgents.Count > 0 ? freeAgents[0].ProjectedPoints : null,
                FreeAgentSecondBest:    freeAgents.Count > 1 ? freeAgents[1].ProjectedPoints : null,
                FreeAgentBestPlayerId:  freeAgents.Count > 0 ? freeAgents[0].PlayerId : null);
        }

        return results;
    }

    /// <summary>
    /// How many players at each position the league starts in aggregate, base slots
    /// plus a greedy allocation of the flex slots.
    ///
    /// <para>
    /// Flex allocation is greedy over the whole slot pool rather than a fixed
    /// RB/WR split: repeatedly take the best remaining player eligible for any
    /// unfilled flex slot, and consume the most restrictive slot that accepts him.
    /// Taking the globally best player each time makes the result independent of the
    /// order the slots happen to be declared in; spending the most restrictive slot
    /// first preserves the flexible ones for later, the way a real manager would.
    /// </para>
    ///
    /// <para>
    /// This is what makes superflex fall out for free. A SUPER_FLEX slot is simply a
    /// flex whose eligible set includes QB, so quarterbacks compete for it on
    /// projection and the QB cutoff moves far deeper — which is the entire reason
    /// elite QBs are worth more in those leagues.
    /// </para>
    /// </summary>
    private static Dictionary<string, int> AllocateStarters(
        Dictionary<string, List<ReplacementCandidate>> byPosition,
        RosterConfiguration config,
        int teamCount)
        => AllocateStarters(
            byPosition.ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlyList<decimal>)kv.Value.Select(c => c.ProjectedPoints).ToList(),
                StringComparer.OrdinalIgnoreCase),
            config,
            teamCount);

    /// <summary>
    /// The same greedy allocation, over positional value curves rather than
    /// candidate records — FAN-153.
    ///
    /// <para>
    /// Allocation never needed player identity: it walks each position's descending
    /// projections and spends flex slots on whoever is best. Taking values only
    /// lets the stored curves
    /// (<see cref="PositionalValueCurveBuilder"/>) feed the identical algorithm, so
    /// a league resolving its replacement level at read time and the live L3 path
    /// cannot drift apart — there is one implementation of the flex rules, not two.
    /// </para>
    ///
    /// <para>
    /// Each list must be sorted descending. Nothing here re-sorts: a curve arrives
    /// sorted from storage, and quietly re-sorting would hide the day one arrives
    /// that is not.
    /// </para>
    /// </summary>
    public static Dictionary<string, int> AllocateStarters(
        IReadOnlyDictionary<string, IReadOnlyList<decimal>> descendingByPosition,
        RosterConfiguration config,
        int teamCount)
    {
        ArgumentNullException.ThrowIfNull(descendingByPosition);
        ArgumentNullException.ThrowIfNull(config);
        if (teamCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(teamCount), teamCount,
                "Team count must be positive — replacement level is meaningless without a league size.");

        var byPosition = ScoredPositions.ToDictionary(
            pos => pos,
            pos => descendingByPosition.TryGetValue(pos, out var v)
                ? v
                : (IReadOnlyList<decimal>)Array.Empty<decimal>(),
            StringComparer.OrdinalIgnoreCase);

        var pointer = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["QB"] = config.QbSlots * teamCount,
            ["RB"] = config.RbSlots * teamCount,
            ["WR"] = config.WrSlots * teamCount,
            ["TE"] = config.TeSlots * teamCount
        };

        // One entry per physical flex slot in the league: each definition, once per team.
        var slots = new List<FlexSlotDefinition>();
        foreach (var def in config.FlexSlotDefinitions)
            for (var i = 0; i < teamCount; i++)
                slots.Add(def);

        while (slots.Count > 0)
        {
            string? bestPos = null;
            var bestPts = decimal.MinValue;

            foreach (var pos in ScoredPositions)
            {
                var idx = pointer[pos];
                if (idx >= byPosition[pos].Count) continue;              // pool exhausted here
                if (!slots.Any(s => s.IsEligible(pos))) continue;        // nothing left he can fill

                var pts = byPosition[pos][idx];
                if (pts > bestPts)
                {
                    bestPts = pts;
                    bestPos = pos;
                }
            }

            // No eligible player left for any remaining slot — those slots simply go
            // unfilled league-wide, which is a real (if unusual) state.
            if (bestPos is null) break;

            // Spend the tightest slot that accepts him, keeping flexible slots free.
            var slotIndex = slots
                .Select((s, i) => (Slot: s, Index: i))
                .Where(x => x.Slot.IsEligible(bestPos))
                .OrderBy(x => x.Slot.EligiblePositions.Count)
                .First().Index;

            slots.RemoveAt(slotIndex);
            pointer[bestPos]++;
        }

        return pointer;
    }

    /// <summary>
    /// Resolves a replacement level per position from stored positional value
    /// curves — FAN-153.
    ///
    /// <para>
    /// This is the read-time half of league-aware replacement level. The curves
    /// come from the nightly simulation; the roster configuration and team count
    /// come from the league asking. Same greedy flex allocation as the live path,
    /// so a SUPER_FLEX slot pulls the QB cutoff deeper here exactly as it does
    /// there.
    /// </para>
    ///
    /// <para>
    /// A cutoff past the end of a curve is reported, never guessed. The two ways
    /// that happens are genuinely different and the caller has to be able to tell
    /// them apart — see <see cref="CurveReplacement"/>.
    /// </para>
    /// </summary>
    public static IReadOnlyDictionary<string, CurveReplacement> ResolveFromCurves(
        IReadOnlyDictionary<string, IReadOnlyList<decimal>> descendingByPosition,
        IReadOnlyDictionary<string, int> poolSizes,
        RosterConfiguration config,
        int teamCount)
    {
        ArgumentNullException.ThrowIfNull(descendingByPosition);
        ArgumentNullException.ThrowIfNull(poolSizes);

        var absorbed = AllocateStarters(descendingByPosition, config, teamCount);
        var results = new Dictionary<string, CurveReplacement>(StringComparer.OrdinalIgnoreCase);

        foreach (var pos in ScoredPositions)
        {
            var curve = descendingByPosition.TryGetValue(pos, out var c)
                ? c
                : (IReadOnlyList<decimal>)Array.Empty<decimal>();
            var poolSize = poolSizes.TryGetValue(pos, out var n) ? n : curve.Count;
            var cutoff = absorbed[pos];

            // Past the stored values, but the pool itself goes deeper — the curve
            // was simply not stored deep enough for this league. Answering with the
            // last stored value would look like an exhausted pool and would inflate
            // every value above it, so the level is left unset and flagged.
            var beyondDepth = cutoff >= curve.Count && cutoff < poolSize;

            // Past the pool itself — the league genuinely starts more players at
            // this position than exist projections for. A real state, and the
            // weakest projection is the honest baseline for it.
            var exhausted = cutoff >= poolSize;

            decimal? level = beyondDepth
                ? null
                : exhausted
                    ? (curve.Count > 0 ? curve[^1] : 0m)
                    : curve[cutoff];

            results[pos] = new CurveReplacement(
                Position:          pos,
                StartersAbsorbed:  cutoff,
                StructuralLevel:   level,
                PoolExhausted:     exhausted,
                BeyondStoredDepth: beyondDepth);
        }

        return results;
    }

    /// <summary>
    /// Value over the structural baseline. The ranking number.
    /// </summary>
    public static decimal StructuralVorp(
        ReplacementCandidate player,
        IReadOnlyDictionary<string, PositionReplacement> levels)
        => levels.TryGetValue(player.Position, out var r)
            ? player.ProjectedPoints - r.StructuralLevel
            : 0m;

    /// <summary>
    /// Value over the best alternative pickup. The waiver number.
    ///
    /// Leave-one-out: the best free agent is measured against the second best, so he
    /// scores his true margin over the next option rather than a meaningless zero.
    /// Returns null when the position has no free agents at all — there is no
    /// alternative pickup to compare against, and 0m would read as "no better than
    /// the wire" when the truth is "the wire is empty".
    /// </summary>
    public static decimal? FreeAgentVorp(
        ReplacementCandidate player,
        IReadOnlyDictionary<string, PositionReplacement> levels)
    {
        if (!levels.TryGetValue(player.Position, out var r)) return null;

        var baseline = player.PlayerId == r.FreeAgentBestPlayerId
            ? r.FreeAgentSecondBest
            : r.FreeAgentBest;

        return baseline is null ? null : player.ProjectedPoints - baseline.Value;
    }
}

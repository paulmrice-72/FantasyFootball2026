using FF.Application.Services;
using FF.Domain.Documents;
using FF.Domain.ValueObjects;
using FluentAssertions;

namespace FF.Tests.Application.Vorp;

/// <summary>
/// FAN-153 phase 1 — the positional value curves, and resolving a league's
/// replacement level out of them.
/// </summary>
public class PositionalValueCurveTests
{
    private const int Season = 2026;

    private static CareerSimulationDocument Sim(
        string id, string position, params double[] seasonValuesByYear)
        => new()
        {
            SleeperPlayerId = id,
            PlayerName = id,
            Position = position,
            Season = Season,
            YearProjections = [.. seasonValuesByYear.Select((v, i) => new CareerYearProjection
            {
                Year = Season + i,
                AgeAtYear = 24 + i,
                SeasonValue = v,
                MedianFppg = v / 17.0
            })]
        };

    /// <summary>
    /// A pool of <paramref name="count"/> players at one position whose year-one
    /// values step down linearly from <paramref name="top"/>. Deterministic, so a
    /// cutoff index maps to a value by arithmetic rather than by inspection.
    /// </summary>
    private static IEnumerable<CareerSimulationDocument> Pool(
        string position, int count, double top, double step)
        => Enumerable.Range(0, count)
            .Select(i => Sim($"{position}{i}", position, top - i * step));

    // ── Builder ────────────────────────────────────────────────────────────

    [Fact]
    public void Build_GroupsByYearAndPosition_AndSortsDescending()
    {
        var sims = new List<CareerSimulationDocument>
        {
            Sim("a", "WR", 100, 90),
            Sim("b", "WR", 300, 50),
            Sim("c", "WR", 200, 70),
            Sim("d", "QB", 400, 380)
        };

        var curves = PositionalValueCurveBuilder.Build(sims);

        var wr2026 = curves.Single(c => c.Year == 2026 && c.Position == "WR");
        wr2026.DescendingSeasonValues.Should().Equal(300, 200, 100);
        wr2026.PoolSize.Should().Be(3);

        // Year two is a separate curve and orders on its own values — b leads 2026
        // and trails 2027, which a per-career baseline could not represent.
        var wr2027 = curves.Single(c => c.Year == 2027 && c.Position == "WR");
        wr2027.DescendingSeasonValues.Should().Equal(90, 70, 50);

        curves.Should().Contain(c => c.Year == 2026 && c.Position == "QB");
    }

    [Fact]
    public void Build_TruncatesToDepth_ButRecordsTheFullPoolSize()
    {
        var curves = PositionalValueCurveBuilder.Build([.. Pool("RB", 40, 300, 5)], depth: 5);

        var rb = curves.Single(c => c.Year == 2026 && c.Position == "RB");
        rb.DescendingSeasonValues.Should().HaveCount(5);
        rb.DescendingSeasonValues[0].Should().Be(300);
        rb.PoolSize.Should().Be(40,
            "a cutoff past the stored values has to be distinguishable from a cutoff " +
            "past the pool itself — the first is a storage limit, the second is a real league state");
    }

    [Fact]
    public void Build_IgnoresUnscoredPositionsAndEmptyProjections()
    {
        var sims = new List<CareerSimulationDocument>
        {
            Sim("k1", "K", 150),
            Sim("d1", "DEF", 140),
            Sim("wr1", "WR", 200),
            new() { SleeperPlayerId = "empty", Position = "WR", Season = Season }
        };

        var curves = PositionalValueCurveBuilder.Build(sims);

        curves.Should().OnlyContain(c => c.Position == "WR");
        // Expected values as an explicit collection, not as params: the params
        // overload of Equal would swallow the because-string as another expected
        // element.
        curves.Single().DescendingSeasonValues.Should().Equal([200.0],
            "a player with no projections contributes nothing — a fabricated zero " +
            "landing at a cutoff would become a replacement level of zero");
    }

    // ── Resolving a league out of the curves ───────────────────────────────

    /// <summary>
    /// FAN-153's own acceptance criterion, at the replacement-level layer: the same
    /// projections must produce a different answer for a superflex league than for
    /// a single-QB one.
    ///
    /// <para>
    /// Twelve teams. Standard plays one QB and a RB/WR/TE flex, so no quarterback
    /// can reach a flex and the cutoff is exactly 12. Superflex adds a slot
    /// quarterbacks are eligible for, they outbid the other positions for all
    /// twelve of them, and the cutoff moves to 24 — a full round deeper, which
    /// lowers the baseline every quarterback is measured against and is the entire
    /// reason elite quarterbacks cost more in that format.
    /// </para>
    /// </summary>
    [Fact]
    public void ResolveFromCurves_SuperflexPushesTheQbCutoffDeeperAndTheBaselineLower()
    {
        var sims = new List<CareerSimulationDocument>();
        sims.AddRange(Pool("QB", 40, 400, 5));
        sims.AddRange(Pool("RB", 60, 300, 3));
        sims.AddRange(Pool("WR", 80, 300, 2.5));
        sims.AddRange(Pool("TE", 40, 200, 3));

        var curves = PositionalValueCurveBuilder.Build(sims);
        var (descending, poolSizes) = Materialise(curves, 2026);

        var standard = ReplacementLevelService.ResolveFromCurves(
            descending, poolSizes, RosterConfiguration.Standard, teamCount: 12);

        var superflex = ReplacementLevelService.ResolveFromCurves(
            descending, poolSizes, RosterConfiguration.Superflex, teamCount: 12);

        standard["QB"].StartersAbsorbed.Should().Be(12,
            "a standard league's only QB-eligible slot is the QB slot itself");
        superflex["QB"].StartersAbsorbed.Should().Be(24,
            "quarterbacks outbid RB/WR/TE for every SUPER_FLEX slot at these values");

        standard["QB"].StructuralLevel.Should().Be(340m);      // 400 - 12 * 5
        superflex["QB"].StructuralLevel.Should().Be(280m);     // 400 - 24 * 5

        superflex["QB"].StructuralLevel.Should().BeLessThan(standard["QB"].StructuralLevel!.Value,
            "this difference IS quarterback scarcity — it is the number the guardrail " +
            "cap tables have been standing in for");

        superflex["QB"].BeyondStoredDepth.Should().BeFalse();
        superflex["QB"].PoolExhausted.Should().BeFalse();
    }

    [Fact]
    public void ResolveFromCurves_CutoffPastStoredDepth_ReturnsNoLevelRatherThanTheLastValue()
    {
        var curves = PositionalValueCurveBuilder.Build([.. Pool("QB", 40, 400, 5)], depth: 5);
        var (descending, poolSizes) = Materialise(curves, 2026);

        var resolved = ReplacementLevelService.ResolveFromCurves(
            descending, poolSizes, RosterConfiguration.Standard, teamCount: 12);

        resolved["QB"].BeyondStoredDepth.Should().BeTrue();
        resolved["QB"].PoolExhausted.Should().BeFalse();
        resolved["QB"].StructuralLevel.Should().BeNull(
            "returning the weakest stored value would read as an exhausted pool and " +
            "inflate every value above it — the fix is a deeper curve, not a guess");
    }

    [Fact]
    public void ResolveFromCurves_CutoffPastThePoolItself_ReturnsTheWeakestProjection()
    {
        var curves = PositionalValueCurveBuilder.Build([.. Pool("QB", 3, 400, 5)]);
        var (descending, poolSizes) = Materialise(curves, 2026);

        var resolved = ReplacementLevelService.ResolveFromCurves(
            descending, poolSizes, RosterConfiguration.Standard, teamCount: 12);

        resolved["QB"].PoolExhausted.Should().BeTrue();
        resolved["QB"].BeyondStoredDepth.Should().BeFalse();
        resolved["QB"].StructuralLevel.Should().Be(390m,
            "the league starts more quarterbacks than exist projections for — a real " +
            "state, and the weakest projection is the honest baseline for it");
    }

    /// <summary>
    /// The curve path and the live L3 path must not drift. Same players, same
    /// league, one through <see cref="ReplacementLevelService.Compute"/> and one
    /// through the stored curves.
    /// </summary>
    [Fact]
    public void ResolveFromCurves_AgreesWithTheLiveReplacementLevelPath()
    {
        var sims = new List<CareerSimulationDocument>();
        sims.AddRange(Pool("QB", 40, 400, 5));
        sims.AddRange(Pool("RB", 60, 300, 3));
        sims.AddRange(Pool("WR", 80, 300, 2.5));
        sims.AddRange(Pool("TE", 40, 200, 3));

        var candidates = sims
            .Select(s => new ReplacementCandidate(
                s.SleeperPlayerId, s.Position, (decimal)s.YearProjections[0].SeasonValue, IsRostered: true))
            .ToList();

        var live = ReplacementLevelService.Compute(
            candidates, RosterConfiguration.Superflex, teamCount: 12);

        var curves = PositionalValueCurveBuilder.Build(sims);
        var (descending, poolSizes) = Materialise(curves, 2026);
        var fromCurves = ReplacementLevelService.ResolveFromCurves(
            descending, poolSizes, RosterConfiguration.Superflex, teamCount: 12);

        foreach (var position in new[] { "QB", "RB", "WR", "TE" })
        {
            fromCurves[position].StartersAbsorbed.Should().Be(live[position].StartersAbsorbed,
                $"{position} cutoff must not depend on which path computed it");
            fromCurves[position].StructuralLevel.Should().Be(live[position].StructuralLevel,
                $"{position} replacement level must not depend on which path computed it");
        }
    }

    private static (IReadOnlyDictionary<string, IReadOnlyList<decimal>> Descending,
                    IReadOnlyDictionary<string, int> PoolSizes)
        Materialise(IReadOnlyList<PositionalValueCurve> curves, int year)
    {
        var forYear = curves.Where(c => c.Year == year).ToList();

        var descending = forYear.ToDictionary(
            c => c.Position,
            c => (IReadOnlyList<decimal>)c.DescendingSeasonValues.Select(v => (decimal)v).ToList(),
            StringComparer.OrdinalIgnoreCase);

        var poolSizes = forYear.ToDictionary(
            c => c.Position, c => c.PoolSize, StringComparer.OrdinalIgnoreCase);

        return (descending, poolSizes);
    }
}

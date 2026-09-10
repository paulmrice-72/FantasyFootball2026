using FF.Application.Services;
using FF.Domain.Documents;
using FF.Domain.Enums;
using FF.Domain.ValueObjects;
using FluentAssertions;

namespace FF.Tests.Application.Vorp;

/// <summary>
/// FAN-153 phase 2 — turning a stored curve into a replacement level, and a
/// career projection into a value over that level.
/// </summary>
public class ValueOverReplacementTests
{
    private const int Season = 2026;
    private const int TeamCount = 12;

    /// <summary>
    /// A curve stepping down by <paramref name="step"/> from
    /// <paramref name="top"/>: index i reads <c>top - step * i</c>, so every
    /// expectation below is arithmetic rather than a recorded output.
    /// </summary>
    private static PositionalValueCurveDocument Curve(
        string position, int year, double top, double step, int poolSize, int? storedDepth = null)
    {
        var stored = storedDepth ?? poolSize;
        var values = Enumerable.Range(0, stored).Select(i => top - step * i).ToList();
        return new PositionalValueCurveDocument
        {
            Id = $"{Season}:Superflex:{year}:{position}",
            Season = Season,
            ScoringFormat = "Superflex",
            Year = year,
            Position = position,
            DescendingSeasonValues = values,
            PoolSize = poolSize,
            Depth = values.Count,
            ComputedAt = DateTime.UtcNow
        };
    }

    private static CareerSimulationDocument FlatSim(
        string position, double seasonValue, int years = 5, int season = Season)
        => new()
        {
            SleeperPlayerId = "p1",
            PlayerName = "p1",
            Position = position,
            Season = season,
            YearProjections = [.. Enumerable.Range(0, years).Select(i => new CareerYearProjection
            {
                Year = season + i,
                AgeAtYear = 26 + i,
                SeasonValue = seasonValue,
                AgingMultiplier = 1.0,
                Phase = CareerPhase.Prime
            })]
        };

    /// <summary>
    /// The measurement phase 1 produced, now on the path the valuation pipeline
    /// actually takes: documents in, levels out.
    ///
    /// <para>
    /// The quarterback baseline falls purely because one slot became QB-eligible.
    /// The receiver baseline does not move at all, and that is not an oversight —
    /// quarterbacks outbid everyone for every SUPER_FLEX slot, so the RB/WR/TE
    /// flex allocation is untouched. If WR ever moves here too, the greedy
    /// allocation has started spending superflex slots on receivers and the
    /// fixture's premise has changed.
    /// </para>
    /// </summary>
    [Fact]
    public void ResolveLevels_SuperflexShape_LowersTheQuarterbackBaselineAndNothingElse()
    {
        var curves = new List<PositionalValueCurveDocument>
        {
            Curve("QB", Season, top: 300, step: 4, poolSize: 40),   // [12] = 252, [24] = 204
            Curve("WR", Season, top: 200, step: 1, poolSize: 80)    // [36] = 164
        };

        var standard = ValueOverReplacementCalculator.ResolveLevels(
            curves, RosterConfiguration.Standard, TeamCount);
        var superflex = ValueOverReplacementCalculator.ResolveLevels(
            curves, RosterConfiguration.Superflex, TeamCount);

        standard[(Season, "QB")].Cutoff.Should().Be(12);
        standard[(Season, "QB")].Level.Should().Be(252);

        superflex[(Season, "QB")].Cutoff.Should().Be(24,
            "twelve SUPER_FLEX slots pull a full extra round of quarterbacks into " +
            "the startable pool");
        superflex[(Season, "QB")].Level.Should().Be(204);

        superflex[(Season, "WR")].Level.Should().Be(standard[(Season, "WR")].Level,
            "quarterbacks win every superflex slot in this fixture, so the receiver " +
            "cutoff is unchanged between the two shapes");
    }

    /// <summary>
    /// A position with no curve gets no level, rather than a level of zero.
    ///
    /// <para>
    /// Zero is not a small replacement level, it is the absence of one, and it
    /// would credit every player at that position with his whole point total as
    /// surplus — putting an unprojected position at the top of the board. The
    /// same class of mistake as a fabricated zero at a cutoff, which is why phase
    /// 1 refused to clamp there either.
    /// </para>
    /// </summary>
    [Fact]
    public void ResolveLevels_PositionWithNoCurve_IsAbsentRatherThanZero()
    {
        var curves = new List<PositionalValueCurveDocument>
        {
            Curve("WR", Season, top: 200, step: 1, poolSize: 80)
        };

        var levels = ValueOverReplacementCalculator.ResolveLevels(
            curves, RosterConfiguration.Standard, TeamCount);

        levels.ContainsKey((Season, "WR")).Should().BeTrue();
        levels.ContainsKey((Season, "QB")).Should().BeFalse();
        levels.ContainsKey((Season, "RB")).Should().BeFalse();
        levels.ContainsKey((Season, "TE")).Should().BeFalse();
    }

    /// <summary>
    /// A cutoff past the stored depth is an error, not an answer. Returning the
    /// weakest stored value would read as an exhausted pool — "this league starts
    /// more receivers than exist" — and inflate every value above it.
    /// </summary>
    [Fact]
    public void ResolveLevels_CutoffPastStoredDepth_ThrowsRatherThanClamping()
    {
        // Eighty receivers in the pool, ten of them stored. A standard league
        // starts at least twenty-four before a single flex slot is spent, so the
        // cutoff is past the stored values while the pool goes far deeper.
        var curves = new List<PositionalValueCurveDocument>
        {
            Curve("WR", Season, top: 200, step: 1, poolSize: 80, storedDepth: 10)
        };

        var act = () => ValueOverReplacementCalculator.ResolveLevels(
            curves, RosterConfiguration.Standard, TeamCount);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*build-value-curves*");
    }

    /// <summary>
    /// The core arithmetic, checkable by hand: a flat surplus of 50 a season for
    /// five seasons, discounted at the quarterback rate of 0.10, is
    /// 50 × (1 + 1/1.1 + 1/1.1² + 1/1.1³ + 1/1.1⁴) = 50 × 4.16987 = 208.49.
    /// </summary>
    [Fact]
    public void Compute_DiscountsEachSeasonsSurplusOverThatSeasonsLevel()
    {
        var curves = Enumerable.Range(Season, 5)
            .Select(year => Curve("QB", year, top: 100, step: 0, poolSize: 40))
            .ToList();

        var levels = ValueOverReplacementCalculator.ResolveLevels(
            curves, RosterConfiguration.Standard, TeamCount);

        var result = ValueOverReplacementCalculator.Compute(
            FlatSim("QB", seasonValue: 150), "QB", discountRate: 0.10, levels);

        result.YearsPriced.Should().Be(5);
        result.YearsWithoutCurve.Should().Be(0);
        result.Total.Should().BeApproximately(208.49, 0.01);
    }

    /// <summary>
    /// Below replacement is a negative number and stays one. Clamping here would
    /// put every sub-replacement player on an identical value, and a tie is not a
    /// ranking.
    /// </summary>
    [Fact]
    public void Compute_BelowReplacement_IsNegative()
    {
        var curves = Enumerable.Range(Season, 5)
            .Select(year => Curve("QB", year, top: 200, step: 0, poolSize: 40))
            .ToList();

        var levels = ValueOverReplacementCalculator.ResolveLevels(
            curves, RosterConfiguration.Standard, TeamCount);

        var result = ValueOverReplacementCalculator.Compute(
            FlatSim("QB", seasonValue: 120), "QB", discountRate: 0.10, levels);

        result.Total.Should().BeApproximately(-80 * 4.169866, 0.01);
    }

    /// <summary>
    /// Where a projection multiplier is applied, pinned as arithmetic.
    ///
    /// <para>
    /// The free-agent penalty is a discount on a player's projected production,
    /// so it multiplies the projection and the replacement level is subtracted
    /// from the result. Applied to the surplus instead — which is what the first
    /// phase 2 run did — a penalised player keeps <c>(1−m)K</c> of value he has
    /// not earned, and on that run it moved RB by −0.0392 while quarterbacks, the
    /// one position the penalty cannot reach, were bit-identical.
    /// </para>
    ///
    /// <para>
    /// Fixture: a flat level of 100, a flat projection of 200, five seasons at a
    /// 0.10 discount. Penalising the projection gives (200 × 0.6) − 100 = 20 a
    /// season. Penalising the surplus would give (200 − 100) × 0.6 = 60 — three
    /// times as much, from the same inputs.
    /// </para>
    /// </summary>
    [Fact]
    public void Compute_ProjectionMultiplier_DiscountsTheProjectionNotTheSurplus()
    {
        var curves = Enumerable.Range(Season, 5)
            .Select(year => Curve("RB", year, top: 100, step: 0, poolSize: 60))
            .ToList();

        var levels = ValueOverReplacementCalculator.ResolveLevels(
            curves, RosterConfiguration.Standard, TeamCount);

        var full = ValueOverReplacementCalculator.Compute(
            FlatSim("RB", seasonValue: 200), "RB", discountRate: 0.10, levels);

        var penalised = ValueOverReplacementCalculator.Compute(
            FlatSim("RB", seasonValue: 200), "RB", discountRate: 0.10, levels,
            projectionMultiplier: 0.6);

        // 100 a season and 20 a season, over the same five discounted seasons.
        full.Total.Should().BeApproximately(100 * 4.169866, 0.01);
        penalised.Total.Should().BeApproximately(20 * 4.169866, 0.01);

        penalised.Total.Should().BeLessThan(full.Total * 0.6,
            "penalising the surplus instead would leave the penalised player at " +
            "exactly 0.6 of the full value — the whole point is that it must be less, " +
            "because the replacement level is subtracted after the discount, not before");
    }

    /// <summary>
    /// The same property stated as the ordering it protects: a discounted player
    /// overtakes a rostered rival at the same place he did before value over
    /// replacement existed — when the rival projects below <c>0.6 ×</c> his gross.
    /// Applied to the surplus, the crossover moves out to
    /// <c>0.6G + 0.4K</c> and every free agent climbs.
    /// </summary>
    [Fact]
    public void Compute_ProjectionMultiplier_LeavesTheCrossoverWhereItWasCalibrated()
    {
        var curves = Enumerable.Range(Season, 5)
            .Select(year => Curve("RB", year, top: 100, step: 0, poolSize: 60))
            .ToList();
        var levels = ValueOverReplacementCalculator.ResolveLevels(
            curves, RosterConfiguration.Standard, TeamCount);

        double Vor(double seasonValue, double multiplier = 1.0)
            => ValueOverReplacementCalculator.Compute(
                FlatSim("RB", seasonValue), "RB", 0.10, levels, multiplier).Total;

        // Free agent projected at 300. The crossover is a rival at 180.
        var freeAgent = Vor(300, multiplier: 0.6);

        Vor(179).Should().BeLessThan(freeAgent, "just under 0.6 x 300");
        Vor(181).Should().BeGreaterThan(freeAgent, "just over 0.6 x 300");
    }

    /// <summary>
    /// A projected season with no curve is skipped and counted, never priced
    /// against nothing. The count is what makes a stale-curve run visible: a
    /// player silently missing two of his five seasons scores low for a reason
    /// that has nothing to do with the player.
    /// </summary>
    [Fact]
    public void Compute_SeasonWithNoCurve_IsCountedRatherThanPricedAgainstZero()
    {
        // Curves for the first three projected seasons only.
        var curves = Enumerable.Range(Season, 3)
            .Select(year => Curve("QB", year, top: 100, step: 0, poolSize: 40))
            .ToList();

        var levels = ValueOverReplacementCalculator.ResolveLevels(
            curves, RosterConfiguration.Standard, TeamCount);

        var result = ValueOverReplacementCalculator.Compute(
            FlatSim("QB", seasonValue: 150, years: 5), "QB", discountRate: 0.10, levels);

        result.YearsPriced.Should().Be(3);
        result.YearsWithoutCurve.Should().Be(2);

        // 50 * (1 + 1/1.1 + 1/1.1^2) = 50 * 2.735537 = 136.78 — the two unpriced
        // seasons contribute nothing at all rather than a full 150 of surplus.
        result.Total.Should().BeApproximately(136.78, 0.01);
    }

    /// <summary>
    /// The defect the first phase 2 run hit, pinned. <c>ScoringFormat</c> pairs a
    /// scoring rule with a roster shape, and a curve is a distribution of projected
    /// points — so only the scoring rule can change it. Curves built under
    /// <c>Superflex</c> and read back under <c>HalfPpr</c> found nothing, because
    /// the pairing was being used as a storage key for something that does not vary
    /// with it.
    /// </summary>
    [Theory]
    [InlineData(ScoringFormat.Standard, "Standard")]
    [InlineData(ScoringFormat.HalfPpr, "HalfPpr")]
    [InlineData(ScoringFormat.Superflex, "HalfPpr")]
    [InlineData(ScoringFormat.FullPpr, "FullPpr")]
    [InlineData(ScoringFormat.SuperflexFullPpr, "FullPpr")]
    public void CurveScoringKey_CollapsesTheRosterShapeOutOfTheFormat(
        ScoringFormat format, string expected)
        => ValueOverReplacementCalculator.CurveScoringKey(format).Should().Be(expected);

    /// <summary>
    /// Stated the other way round, because this is the property the storage key
    /// exists to have: a superflex format and its 1-QB counterpart must read the
    /// same curve, or "the same projections" in this ticket's acceptance criterion
    /// is not true of the data.
    /// </summary>
    [Fact]
    public void CurveScoringKey_SuperflexAndItsOneQbCounterpart_ShareAKey()
    {
        ValueOverReplacementCalculator.CurveScoringKey(ScoringFormat.Superflex)
            .Should().Be(ValueOverReplacementCalculator.CurveScoringKey(ScoringFormat.HalfPpr));

        ValueOverReplacementCalculator.CurveScoringKey(ScoringFormat.SuperflexFullPpr)
            .Should().Be(ValueOverReplacementCalculator.CurveScoringKey(ScoringFormat.FullPpr));
    }

    [Theory]
    [InlineData(ScoringFormat.Superflex)]
    [InlineData(ScoringFormat.SuperflexFullPpr)]
    public void LeagueShapeFor_SuperflexFormats_CarryASuperFlexSlot(ScoringFormat format)
        => ValueOverReplacementCalculator.LeagueShapeFor(format)
            .FlexSlotDefinitions.Should()
            .Contain(d => d.IsEligible("QB"));

    [Theory]
    [InlineData(ScoringFormat.Standard)]
    [InlineData(ScoringFormat.HalfPpr)]
    [InlineData(ScoringFormat.FullPpr)]
    public void LeagueShapeFor_SingleQbFormats_HaveNoQbEligibleFlex(ScoringFormat format)
        => ValueOverReplacementCalculator.LeagueShapeFor(format)
            .FlexSlotDefinitions.Should()
            .NotContain(d => d.IsEligible("QB"));
}

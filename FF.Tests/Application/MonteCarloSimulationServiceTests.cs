// FF.Tests/Application/MonteCarloSimulationServiceTests.cs
using FF.Application.Services;
using FF.Domain.Documents;
using FF.Domain.Enums;
using FluentAssertions;

namespace FF.Tests.Application;

public class MonteCarloSimulationServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static PlayerProjectionDocument Projection(
        decimal halfPprProjection = 20m,
        string position = "WR") =>
        new()
        {
            PlayerId = "test-player",
            PlayerName = "Test Player",
            Position = position,
            NflTeam = "KC",
            OpponentTeam = "LV",
            Season = 2024,
            Week = 18,
            ProjectedPointsHalfPpr = halfPprProjection,
            ProjectedPoints = halfPprProjection * 0.85m,
            ProjectedPointsPpr = halfPprProjection * 1.15m,
            ScoringFormat = "HalfPpr"
        };

    // ── Distribution Shape Tests ──────────────────────────────────────────

    [Fact]
    public void Simulate_Floor_IsLessThanMedian()
    {
        var result = MonteCarloSimulationService.Simulate(Projection(), seed: 42);
        result.Floor.Should().BeLessThan(result.Median);
    }

    [Fact]
    public void Simulate_Median_IsLessThanCeiling()
    {
        var result = MonteCarloSimulationService.Simulate(Projection(), seed: 42);
        result.Median.Should().BeLessThan(result.Ceiling);
    }

    [Fact]
    public void Simulate_Mean_IsApproximatelyBaseProjection()
    {
        // With 10k iterations the mean should converge close to base projection.
        // Simulate() bases its distribution on ProjectedPointsPpr (full-PPR stopgap,
        // FAN-97, 2026-08-23) rather than ProjectedPointsHalfPpr — assert against
        // that field directly instead of the raw half-PPR fixture input.
        var projection = Projection(20m);
        var result = MonteCarloSimulationService.Simulate(projection, seed: 42);
        result.Mean.Should().BeApproximately(projection.ProjectedPointsPpr, 2.0m,
            "mean of normal distribution should converge near the base projection");
    }

    [Fact]
    public void Simulate_FloorIsNeverNegative()
    {
        var result = MonteCarloSimulationService.Simulate(Projection(2m), seed: 42);
        result.Floor.Should().BeGreaterThanOrEqualTo(0m,
            "projection distribution is clamped at zero");
    }

    [Fact]
    public void Simulate_IterationCountStoredOnResult()
    {
        var result = MonteCarloSimulationService.Simulate(
            Projection(), iterations: 1000, seed: 42);
        result.Iterations.Should().Be(1000);
    }

    // ── Role Variance Tests ───────────────────────────────────────────────

    [Fact]
    public void Simulate_DeepThreat_HasWiderSpreadThanSlotReceiver()
    {
        var proj = Projection(20m, "WR");

        var deepThreat = MonteCarloSimulationService.Simulate(
            proj, PlayerRole.DeepThreat, seed: 42);
        var slot = MonteCarloSimulationService.Simulate(
            proj, PlayerRole.SlotPossession, seed: 42);

        var deepThreatSpread = deepThreat.Ceiling - deepThreat.Floor;
        var slotSpread = slot.Ceiling - slot.Floor;

        deepThreatSpread.Should().BeGreaterThan(slotSpread,
            "deep threats have higher variance than slot receivers");
    }

    [Fact]
    public void Simulate_BellCow_HasNarrowerSpreadThanHandcuff()
    {
        var proj = Projection(15m, "RB");

        var bellCow = MonteCarloSimulationService.Simulate(
            proj, PlayerRole.BellCow, seed: 42);
        var handcuff = MonteCarloSimulationService.Simulate(
            proj, PlayerRole.Handcuff, seed: 42);

        var bellCowSpread = bellCow.Ceiling - bellCow.Floor;
        var handcuffSpread = handcuff.Ceiling - handcuff.Floor;

        bellCowSpread.Should().BeLessThan(handcuffSpread,
            "bell cows are more predictable than handcuffs");
    }

    [Fact]
    public void Simulate_StandardDeviation_HigherForHighVarianceRole()
    {
        var proj = Projection(20m, "WR");

        var deepThreat = MonteCarloSimulationService.Simulate(
            proj, PlayerRole.DeepThreat, seed: 42);
        var wr1 = MonteCarloSimulationService.Simulate(
            proj, PlayerRole.WR1Alpha, seed: 42);

        deepThreat.StandardDeviation.Should().BeGreaterThan(wr1.StandardDeviation,
            "deep threats have higher standard deviation than WR1 alphas");
    }

    // ── Boom/Bust Tests ───────────────────────────────────────────────────

    [Fact]
    public void Simulate_BoomProbability_IsBetweenZeroAndOne()
    {
        var result = MonteCarloSimulationService.Simulate(Projection(), seed: 42);
        result.BoomProbability.Should().BeInRange(0m, 1m);
    }

    [Fact]
    public void Simulate_BustProbability_IsBetweenZeroAndOne()
    {
        var result = MonteCarloSimulationService.Simulate(Projection(), seed: 42);
        result.BustProbability.Should().BeInRange(0m, 1m);
    }

    [Fact]
    public void Simulate_ZeroBaseProjection_BustProbabilityIsOne()
    {
        var result = MonteCarloSimulationService.Simulate(Projection(0m), seed: 42);
        result.BustProbability.Should().Be(1m,
            "a player projected for zero points is always a bust");
    }

    [Fact]
    public void Simulate_HighBaseProjection_LowerBustProbability()
    {
        // Use large iteration count and different seeds to get stable distribution comparison
        var highProj = MonteCarloSimulationService.Simulate(Projection(40m), seed: 42);
        var lowProj = MonteCarloSimulationService.Simulate(Projection(5m), seed: 99);

        highProj.BustProbability.Should().BeLessThanOrEqualTo(lowProj.BustProbability,
            "high projected scorers are less likely to bust than low projected scorers");
    }

    // ── Document Mapping Tests ────────────────────────────────────────────

    [Fact]
    public void Simulate_PlayerIdentityMappedCorrectly()
    {
        var proj = Projection();
        var result = MonteCarloSimulationService.Simulate(proj, seed: 42);

        result.PlayerId.Should().Be(proj.PlayerId);
        result.PlayerName.Should().Be(proj.PlayerName);
        result.Position.Should().Be(proj.Position);
        result.NflTeam.Should().Be(proj.NflTeam);
        result.Season.Should().Be(proj.Season);
        result.Week.Should().Be(proj.Week);
    }

    [Fact]
    public void Simulate_RoleStoredAsString()
    {
        var result = MonteCarloSimulationService.Simulate(
            Projection(), PlayerRole.WR1Alpha, seed: 42);
        result.PlayerRole.Should().Be("WR1Alpha");
    }

    [Fact]
    public void Simulate_SeedProducesDeterministicResults()
    {
        var proj = Projection();
        var result1 = MonteCarloSimulationService.Simulate(proj, seed: 123);
        var result2 = MonteCarloSimulationService.Simulate(proj, seed: 123);

        result1.Floor.Should().Be(result2.Floor);
        result1.Median.Should().Be(result2.Median);
        result1.Ceiling.Should().Be(result2.Ceiling);
    }
}
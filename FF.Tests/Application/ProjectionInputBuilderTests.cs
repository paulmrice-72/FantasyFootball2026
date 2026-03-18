// FF.Tests/Application/ProjectionInputBuilderTests.cs
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Domain.ValueObjects;
using FluentAssertions;

namespace FF.Tests.Application;

public class ProjectionInputBuilderTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static ProjectionInput BuildInput(
        decimal matchupDifficultyScore = 50m,
        int gameLogs = 5) =>
        new()
        {
            PlayerId = "test-player",
            Position = "WR",
            MatchupDifficultyScore = matchupDifficultyScore,
            SnapPct = 0.75m,
            TargetShare = 0.22m,
            GameLogs = [.. Enumerable.Range(1, gameLogs)
                .Select(w => new PlayerGameLogDocument
                {
                    PlayerId = "test-player",
                    Season = 2024,
                    Week = w,
                    SeasonType = "REG",
                    Position = "WR",
                    Targets = 6,
                    ReceivingYards = 70m,
                    FantasyPoints = 10m,
                    FantasyPointsPpr = 16m
                })],
            Weights = ProjectionWeightProfile.Default
        };

    // ── MatchupAdjustmentFactor Tests ─────────────────────────────────────

    [Fact]
    public void Project_NeutralMatchup_FactorIsOne()
    {
        // DifficultyScore 50 = neutral → factor must be exactly 1.0
        var input = BuildInput(matchupDifficultyScore: 50m);
        var result = PlayerProjectionService.Project(input);

        result.MatchupAdjustmentFactor.Should().Be(1.0m,
            "difficulty score 50 is defined as neutral and must produce factor 1.0");
    }

    [Fact]
    public void Project_ToughMatchup_DeflatesProjection()
    {
        // DifficultyScore 100 = toughest possible → factor = 1 + ((50-100)/50)*0.20 = 0.80
        var input = BuildInput(matchupDifficultyScore: 100m);
        var result = PlayerProjectionService.Project(input);

        result.MatchupAdjustmentFactor.Should().BeApproximately(0.80m, 0.01m,
            "the toughest matchup should deflate projections by 20%");

        result.ProjectedPointsPpr.Should().BeLessThan(
            PlayerProjectionService.Project(BuildInput(50m)).ProjectedPointsPpr,
            "tough matchup projection must be lower than neutral");
    }

    [Fact]
    public void Project_EasyMatchup_InflatesProjection()
    {
        // DifficultyScore 0 = easiest possible → factor = 1 + ((50-0)/50)*0.20 = 1.20
        var input = BuildInput(matchupDifficultyScore: 0m);
        var result = PlayerProjectionService.Project(input);

        result.MatchupAdjustmentFactor.Should().BeApproximately(1.20m, 0.01m,
            "the easiest matchup should inflate projections by 20%");

        result.ProjectedPointsPpr.Should().BeGreaterThan(
            PlayerProjectionService.Project(BuildInput(50m)).ProjectedPointsPpr,
            "easy matchup projection must be higher than neutral");
    }

    [Fact]
    public void Project_ProjectedPointsNeverNegative()
    {
        // Even with a very tough matchup and low base stats, output must be >= 0
        var input = BuildInput(matchupDifficultyScore: 100m);
        var result = PlayerProjectionService.Project(input);

        result.ProjectedPoints.Should().BeGreaterThanOrEqualTo(0m);
        result.ProjectedPointsPpr.Should().BeGreaterThanOrEqualTo(0m);
        result.ProjectedPointsHalfPpr.Should().BeGreaterThanOrEqualTo(0m);
    }

    [Fact]
    public void Project_FactorScalesSymmetrically()
    {
        // Score 25 should produce a factor equidistant from 1.0 as score 75
        var easy = PlayerProjectionService.Project(BuildInput(25m));
        var tough = PlayerProjectionService.Project(BuildInput(75m));

        var easyDelta = easy.MatchupAdjustmentFactor - 1.0m;
        var toughDelta = 1.0m - tough.MatchupAdjustmentFactor;

        easyDelta.Should().BeApproximately(toughDelta, 0.001m,
            "the adjustment formula should be symmetric around the neutral score of 50");
    }
}
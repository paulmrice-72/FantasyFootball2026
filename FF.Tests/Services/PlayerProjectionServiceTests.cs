// FF.Tests/Services/PlayerProjectionServiceTests.cs

using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Domain.ValueObjects;
using FluentAssertions;

public class PlayerProjectionServiceTests
{
    private readonly PlayerProjectionService _sut = new();

    private static ProjectionInput BuildInput(int gameCount, decimal pointsPerGame, decimal difficultyScore = 50m)
    {
        var logs = Enumerable.Range(0, gameCount)
            .Select(i => new PlayerGameLogDocument
            {
                FantasyPoints = pointsPerGame,
                FantasyPointsPpr = pointsPerGame + 2m,
                OffenseSnaps = 40,
                Week = 18 - i,
                Season = 2024
            }).ToList();

        return new ProjectionInput
        {
            PlayerId = "test-player",
            Position = "WR",
            GameLogs = logs,
            SnapPct = 0.75m,
            TargetShare = 0.22m,
            MatchupDifficultyScore = difficultyScore,
            Weights = ProjectionWeightProfile.Default
        };
    }

    [Fact]
    public void Project_ReturnsInsufficient_WhenTooFewGames()
    {
        var input = BuildInput(2, 15m);
        var result = PlayerProjectionService.Project(input);
        result.IsInsufficient.Should().BeTrue();
    }

    [Fact]
    public void Project_ReturnsPositiveProjection_ForSufficientData()
    {
        var input = BuildInput(8, 15m);
        var result = PlayerProjectionService.Project(input);
        result.IsInsufficient.Should().BeFalse();
        result.ProjectedPointsHalfPpr.Should().BeGreaterThan(0m);
    }

    [Fact]
    public void Project_MatchupAdjustment_EasyOpponent_IncreasesProjection()
    {
        var neutral = PlayerProjectionService.Project(BuildInput(8, 15m, difficultyScore: 50m));
        var easy = PlayerProjectionService.Project(BuildInput(8, 15m, difficultyScore: 10m));
        easy.ProjectedPointsHalfPpr.Should().BeGreaterThan(neutral.ProjectedPointsHalfPpr);
    }

    [Fact]
    public void Project_MatchupAdjustment_HardOpponent_DecreasesProjection()
    {
        var neutral = PlayerProjectionService.Project(BuildInput(8, 15m, difficultyScore: 50m));
        var hard = PlayerProjectionService.Project(BuildInput(8, 15m, difficultyScore: 90m));
        hard.ProjectedPointsHalfPpr.Should().BeLessThan(neutral.ProjectedPointsHalfPpr);
    }

    [Fact]
    public void Project_NeverReturnsNegativeProjection()
    {
        var input = BuildInput(8, 0m, difficultyScore: 100m);
        var result = PlayerProjectionService.Project(input);
        result.ProjectedPoints.Should().BeGreaterThanOrEqualTo(0m);
    }
}
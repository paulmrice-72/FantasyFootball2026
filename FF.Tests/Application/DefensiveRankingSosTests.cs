// FF.Tests/Application/DefensiveRankingSosTests.cs
using FF.Domain.Documents;
using FluentAssertions;

namespace FF.Tests.Application;

public class DefensiveRankingSosTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static PlayerGameLogDocument Log(
        string playerId, string nflTeam, string opponentTeam,
        string position, int season, int week, decimal fantasyPts) =>
        new()
        {
            PlayerId = playerId,
            NflTeam = nflTeam,
            OpponentTeam = opponentTeam,
            Position = position,
            Season = season,
            Week = week,
            SeasonType = "REG",
            FantasyPointsPpr = fantasyPts
        };

    // Inline the SOS factor calculation (mirrors DefensiveRankingService logic)
    private static decimal ApplySosFactor(decimal difficultyScore,
        double avgOpponentStrength, double leagueAvgOffense)
    {
        var sosFactor = leagueAvgOffense > 0
            ? avgOpponentStrength / leagueAvgOffense
            : 1.0;

        return (decimal)Math.Round(
            Math.Clamp((double)difficultyScore * sosFactor, 0.0, 100.0), 1);
    }

    // ── SOS Factor Tests ──────────────────────────────────────────────────

    [Fact]
    public void SosFactor_WeakSchedule_DeflatesDifficultyScore()
    {
        // Defense faced offenses averaging 10 pts — league avg is 20
        // Factor = 10 / 20 = 0.5 → score should be cut roughly in half
        var rawScore = 80m;
        var result = ApplySosFactor(rawScore,
            avgOpponentStrength: 10.0,
            leagueAvgOffense: 20.0);

        result.Should().BeLessThan(rawScore,
            "a defense that feasted on weak offenses should have its score deflated");
        result.Should().BeApproximately(40.0m, 0.5m);
    }

    [Fact]
    public void SosFactor_StrongSchedule_InflatesDifficultyScore()
    {
        // Defense faced offenses averaging 30 pts — league avg is 20
        // Factor = 30 / 20 = 1.5 → score should increase
        var rawScore = 50m;
        var result = ApplySosFactor(rawScore,
            avgOpponentStrength: 30.0,
            leagueAvgOffense: 20.0);

        result.Should().BeGreaterThan(rawScore,
            "a defense that faced strong offenses should have its score bumped");
        result.Should().BeApproximately(75.0m, 0.5m);
    }

    [Fact]
    public void SosFactor_NeutralSchedule_LeavesScoreUnchanged()
    {
        // Opponent strength equals league average → factor = 1.0
        var rawScore = 60m;
        var result = ApplySosFactor(rawScore,
            avgOpponentStrength: 20.0,
            leagueAvgOffense: 20.0);

        result.Should().BeApproximately(rawScore, 0.1m,
            "neutral schedule should not change the difficulty score");
    }

    [Fact]
    public void SosFactor_Clamps_AtOneHundred()
    {
        // High raw score + strong schedule multiplier — must not exceed 100
        var rawScore = 90m;
        var result = ApplySosFactor(rawScore,
            avgOpponentStrength: 40.0,
            leagueAvgOffense: 20.0);

        result.Should().BeLessThanOrEqualTo(100m,
            "SOS-adjusted score must never exceed 100");
    }

    [Fact]
    public void SosFactor_ZeroLeagueAverage_ReturnsDifficultyScoreUnchanged()
    {
        // Guard against division by zero — factor defaults to 1.0
        var rawScore = 55m;
        var result = ApplySosFactor(rawScore,
            avgOpponentStrength: 20.0,
            leagueAvgOffense: 0.0);

        result.Should().BeApproximately(rawScore, 0.1m,
            "zero league average should fall back to factor 1.0");
    }

    // ── Document Field Test ───────────────────────────────────────────────

    [Fact]
    public void DefensiveRankingDocument_HasSosAdjustedDifficultyScore()
    {
        // Verify the new field exists and is independently settable from DifficultyScore
        var doc = new DefensiveRankingDocument
        {
            DifficultyScore = 70m,
            SosAdjustedDifficultyScore = 55m
        };

        doc.SosAdjustedDifficultyScore.Should().Be(55m);
        doc.DifficultyScore.Should().Be(70m);
        doc.SosAdjustedDifficultyScore.Should().NotBe(doc.DifficultyScore,
            "the two scores should be independently stored");
    }
}
namespace FF.Application.Features.Matchup
{
    public record MatchupDifficultyResult(
        string Team,
        string Position,
        int Season,
        int Week,
        decimal DifficultyScore,
        decimal SosAdjustedDifficultyScore,    // ← new
        decimal SeasonPercentile,
        decimal L4WPercentile,
        decimal AvgPointsAllowed,
        decimal AvgPointsAllowedL4W,
        int GamesAllowed);
}
// FF.Application/Services/ProjectionInputBuilder.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Domain.ValueObjects;

namespace FF.Application.Interfaces.Services;

public class ProjectionInputBuilder(
    IPlayerGameLogRepository gameLogs,
    IPlayerUsageMetricsRepository usageMetrics,
    IDefensiveRankingRepository defRankings)
{
    private readonly IPlayerGameLogRepository _gameLogs = gameLogs;
    private readonly IPlayerUsageMetricsRepository _usageMetrics = usageMetrics;
    private readonly IDefensiveRankingRepository _defRankings = defRankings;

    public async Task<ProjectionInput?> BuildAsync(
        string playerId,
        string position,
        string opponentTeam,
        int season,
        int week,
        ProjectionWeightProfile weights,
        CancellationToken ct = default)
    {
        var logs = await _gameLogs.GetRecentAsync(playerId, season, week, weights.LookbackWeeks, ct);
        if (logs.Count < weights.MinGamesRequired) return null;

        var usage = await _usageMetrics.GetByPlayerIdAsync(playerId, season, ct);
        var matchup = await _defRankings.GetByTeamPositionAsync(opponentTeam, position, season, week, ct);

        // Use SOS-adjusted score if available, fall back to raw difficulty score,
        // then fall back to neutral (50) if no ranking found at all.
        var difficultyScore = matchup is null
            ? 50m
            : matchup.SosAdjustedDifficultyScore > 0
                ? matchup.SosAdjustedDifficultyScore
                : matchup.DifficultyScore;

        return new ProjectionInput
        {
            PlayerId = playerId,
            Position = position,
            GameLogs = logs,
            SnapPct = usage?.SnapPct3Wk ?? 0m,
            TargetShare = usage?.TargetShare3Wk ?? 0m,
            MatchupDifficultyScore = difficultyScore,
            Weights = weights
        };
    }
}

public class ProjectionInput
{
    public string PlayerId { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public IReadOnlyList<PlayerGameLogDocument> GameLogs { get; set; } = [];
    public decimal SnapPct { get; set; }
    public decimal TargetShare { get; set; }
    public decimal MatchupDifficultyScore { get; set; }
    public ProjectionWeightProfile Weights { get; set; } = ProjectionWeightProfile.Default;
}
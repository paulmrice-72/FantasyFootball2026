// FF.Application/Services/ProjectionInputBuilder.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Services;
using FF.Domain.Documents;
using FF.Domain.Enums;
using FF.Domain.ValueObjects;

namespace FF.Application.Interfaces.Services;

/// <summary>
/// Assembles the inputs the projection layers need. All repository access for a
/// projection run happens here, which is what keeps StatLineProjectionService pure.
/// </summary>
public class ProjectionInputBuilder(
    IPlayerGameLogRepository gameLogs,
    IPlayerUsageMetricsRepository usageMetrics,
    IDefensiveRankingRepository defRankings)
{
    private readonly IPlayerGameLogRepository _gameLogs = gameLogs;
    private readonly IPlayerUsageMetricsRepository _usageMetrics = usageMetrics;
    private readonly IDefensiveRankingRepository _defRankings = defRankings;

    /// <summary>
    /// Builds the L0 input for one player (Epic 20 / FAN-116).
    ///
    /// <paramref name="basis"/> and <paramref name="basisSeason"/> are resolved once
    /// per run by the caller, not guessed here — that is what makes the preseason
    /// carryover explicit instead of a silent fallback buried in a repository.
    /// </summary>
    public async Task<StatLineProjectionInput?> BuildStatLineInputAsync(
        string playerId,
        string position,
        string opponentTeam,
        int requestSeason,
        int week,
        ProjectionBasis basis,
        int basisSeason,
        CorrelationMetadata? gameScript,
        ProjectionWeightProfile weights,
        CancellationToken ct = default)
    {
        // Current season → the trailing lookback window ending at the requested week.
        // Carryover → the whole prior season, because there is no "recent" to window.
        IReadOnlyList<PlayerGameLogDocument> logs =
            basis == ProjectionBasis.PriorSeasonCarryover
                ? await _gameLogs.GetByPlayerSeasonAsync(playerId, basisSeason, ct)
                : await _gameLogs.GetRecentAsync(playerId, basisSeason, week, weights.LookbackWeeks, ct);

        if (logs.Count == 0) return null;

        var usage = await _usageMetrics.GetByPlayerIdAsync(playerId, basisSeason, ct);

        // Matchup is always looked up against the season/week being PROJECTED, not
        // the basis season — a carryover projection for 2026 Week 1 still faces a
        // 2026 opponent. No ranking yet in preseason → neutral 50.
        var matchup = await _defRankings.GetByTeamPositionAsync(
            opponentTeam, position, requestSeason, week, ct);

        var difficultyScore = matchup is null
            ? 50m
            : matchup.SosAdjustedDifficultyScore > 0
                ? matchup.SosAdjustedDifficultyScore
                : matchup.DifficultyScore;

        return new StatLineProjectionInput
        {
            PlayerId = playerId,
            Position = position,
            GameLogs = logs,
            Basis = basis,
            BasisSeason = basisSeason,
            Usage = usage,
            MatchupDifficultyScore = difficultyScore,
            GameScriptRbMultiplier = gameScript?.RbVolumeMultiplier ?? 1.0m,
            GameScriptWrTeMultiplier = gameScript?.WrTeVolumeMultiplier ?? 1.0m,
            AgeAdjustmentFactor = 1.0m,   // wired to aging curves in PROJ-004 (FAN-119)
            Weights = weights
        };
    }

    /// <summary>
    /// Legacy points-regression input (pre-Epic 20). Retained so existing callers of
    /// PlayerProjectionService keep compiling; the projection job no longer uses it.
    /// </summary>
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

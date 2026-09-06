// FF.Infrastructure/Services/UsageMetricsService.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services.Usage;
using FF.Application.Services;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

public class UsageMetricsService(
    IPlayerGameLogRepository gameLogRepository,
    IPlayerUsageMetricsRepository metricsRepository,
    ILogger<UsageMetricsService> logger) : IUsageMetricsService
{
    private readonly IPlayerGameLogRepository _gameLogRepository = gameLogRepository;
    private readonly IPlayerUsageMetricsRepository _metricsRepository = metricsRepository;
    private readonly ILogger<UsageMetricsService> _logger = logger;

    public async Task AggregatePlayerMetricsAsync(
        string playerId,
        int season,
        CancellationToken ct = default)
    {
        var gameLogs = await _gameLogRepository
            .GetByPlayerSeasonAsync(playerId, season, ct);

        var activeLogs = gameLogs
            .Where(g => g.Targets > 0 || g.Carries > 0 ||
                        g.Completions > 0 || g.SpecialTeamsTds > 0)
            .OrderBy(g => g.Week)
            .ToList();

        if (activeLogs.Count == 0)
        {
            _logger.LogDebug(
                "No active game logs for player {PlayerId} season {Season}",
                playerId, season);
            return;
        }

        // Rolling windows — take last N games from the ordered list
        var recent3 = activeLogs.TakeLast(3).ToList();
        var recent5 = activeLogs.TakeLast(5).ToList();

        var targetShares = activeLogs.Select(g => g.TargetShare).ToList();
        var airYardsShares = activeLogs.Select(g => g.AirYardsShare).ToList();
        var woprs = activeLogs.Select(g => g.Wopr).ToList();
        var snapPcts = activeLogs.Select(g => g.SnapPct).ToList();

        var carryShares = activeLogs
            .Select(g => g.Carries > 0 ? (decimal)g.Carries : 0m)
            .ToList();

        var lastLog = activeLogs.Last();

        var metrics = new PlayerUsageMetricsDocument
        {
            PlayerId = playerId,
            PlayerName = lastLog.PlayerName,
            NflTeam = lastLog.NflTeam,
            Position = lastLog.Position,
            Season = season,

            // Target Share
            TargetShare3Wk = UsageMetricsCalculator.WeightedAverage(targetShares, 3),
            TargetShare5Wk = UsageMetricsCalculator.WeightedAverage(targetShares, 5),
            TargetShareSeason = UsageMetricsCalculator.SimpleAverage(targetShares),

            // Air Yards Share
            AirYardsShare3Wk = UsageMetricsCalculator.WeightedAverage(airYardsShares, 3),
            AirYardsShare5Wk = UsageMetricsCalculator.WeightedAverage(airYardsShares, 5),
            AirYardsShareSeason = UsageMetricsCalculator.SimpleAverage(airYardsShares),

            // WOPR
            Wopr3Wk = UsageMetricsCalculator.WeightedAverage(woprs, 3),
            Wopr5Wk = UsageMetricsCalculator.WeightedAverage(woprs, 5),
            WoprSeason = UsageMetricsCalculator.SimpleAverage(woprs),

            // Carry Share
            CarryShare3Wk = UsageMetricsCalculator.WeightedAverage(carryShares, 3),
            CarryShare5Wk = UsageMetricsCalculator.WeightedAverage(carryShares, 5),
            CarryShareSeason = UsageMetricsCalculator.SimpleAverage(carryShares),

            // Snap Percentage
            SnapPct3Wk = UsageMetricsCalculator.WeightedAverage(snapPcts, 3),
            SnapPct5Wk = UsageMetricsCalculator.WeightedAverage(snapPcts, 5),
            SnapPctSeason = UsageMetricsCalculator.SimpleAverage(snapPcts),

            // aDOT — Average Depth of Target (ReceivingAirYards / Targets)
            ADot3Wk = CalculateADot(recent3),
            ADot5Wk = CalculateADot(recent5),
            ADotSeason = CalculateADot(activeLogs),

            // TPRR — Targets Per Route Run (Targets / OffenseSnaps as proxy)
            Tprr3Wk = CalculateTprr(recent3),
            Tprr5Wk = CalculateTprr(recent5),
            TprrSeason = CalculateTprr(activeLogs),

            CalculatedAt = DateTime.UtcNow,
            DataWeeksAvailable = activeLogs.Count
        };

        // Classify role after metrics are calculated
        metrics.Role = RoleClassificationService.Classify(metrics);
        metrics.RoleClassifiedAt = DateTime.UtcNow;

        await _metricsRepository.UpsertAsync(metrics, ct);

        _logger.LogInformation(
            "Aggregated usage metrics for {PlayerId} season {Season} — {Weeks} weeks, Role: {Role}",
            playerId, season, activeLogs.Count, metrics.Role);
    }

    public async Task<int> AggregateAllPlayersAsync(
        int season,
        CancellationToken ct = default)
    {
        var playerIds = await _gameLogRepository
            .GetDistinctPlayerIdsAsync(season, ct);

        // A season with no game logs is a no-op, not a success. Say so loudly —
        // the job previously ran, wrote nothing, and reported completion.
        if (playerIds.Count == 0)
        {
            _logger.LogWarning(
                "Usage aggregation found NO game logs for season {Season} — nothing was written. "
                + "Check that the stats sync has run for that season.",
                season);
            return 0;
        }

        _logger.LogInformation(
            "Starting usage aggregation for {Count} players — season {Season}",
            playerIds.Count, season);

        foreach (var batch in playerIds.Chunk(10))
        {
            await Task.WhenAll(
                batch.Select(id => AggregatePlayerMetricsAsync(id, season, ct)));
        }

        _logger.LogInformation(
            "Completed usage aggregation for season {Season} — {Count} players processed",
            season, playerIds.Count);

        return playerIds.Count;
    }

    // aDOT = ReceivingAirYards / Targets
    private static decimal CalculateADot(List<PlayerGameLogDocument> logs)
    {
        var totalTargets = logs.Sum(g => g.Targets);
        if (totalTargets == 0) return 0m;
        var totalAirYards = logs.Sum(g => g.ReceivingAirYards);
        return totalAirYards / totalTargets;
    }

    // TPRR = Targets / OffenseSnaps (snap count as route proxy)
    private static decimal CalculateTprr(List<PlayerGameLogDocument> logs)
    {
        var totalSnaps = logs.Sum(g => g.OffenseSnaps);
        if (totalSnaps == 0) return 0m;
        var totalTargets = logs.Sum(g => g.Targets);
        return (decimal)totalTargets / totalSnaps;
    }
}
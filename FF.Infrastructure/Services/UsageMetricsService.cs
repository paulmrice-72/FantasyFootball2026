// FF.Infrastructure/Services/UsageMetricsService.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services.Usage;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;

namespace FF.Infrastructure.Services;

public class UsageMetricsService(
    IPlayerGameLogRepository gameLogRepository,
    IUsageMetricsRepository metricsRepository,
    ILogger<UsageMetricsService> logger) : IUsageMetricsService
{
    private readonly IPlayerGameLogRepository _gameLogRepository = gameLogRepository;
    private readonly IUsageMetricsRepository _metricsRepository = metricsRepository;
    private readonly ILogger<UsageMetricsService> _logger = logger;

    public async Task AggregatePlayerMetricsAsync(
        string playerId,
        int season,
        CancellationToken ct = default)
    {
        var gameLogs = await _gameLogRepository
            .GetByPlayerSeasonAsync(playerId, season, ct);

        // Exclude weeks where the player had zero snaps (DNP / inactive)
        // SnapPct not yet on document — using Targets + Carries as proxy for active
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

        var targetShares = activeLogs.Select(g => g.TargetShare).ToList();
        var airYardsShares = activeLogs.Select(g => g.AirYardsShare).ToList();
        var woprs = activeLogs.Select(g => g.Wopr).ToList();

        // CarryShare: Carries / team total carries not on document yet
        // Store raw carries for now — will refine when snap/team data added
        var carryShares = activeLogs
            .Select(g => g.Carries > 0 ? (decimal)g.Carries : 0m)
            .ToList();

        var metrics = new PlayerUsageMetricsDocument
        {
            PlayerId = playerId,
            Season = season,
            Position = activeLogs.Last().Position,

            TargetShare3Wk = UsageMetricsCalculator.WeightedAverage(targetShares, 3),
            TargetShare5Wk = UsageMetricsCalculator.WeightedAverage(targetShares, 5),
            TargetShareSeason = UsageMetricsCalculator.SimpleAverage(targetShares),

            AirYardsShare3Wk = UsageMetricsCalculator.WeightedAverage(airYardsShares, 3),
            AirYardsShare5Wk = UsageMetricsCalculator.WeightedAverage(airYardsShares, 5),
            AirYardsShareSeason = UsageMetricsCalculator.SimpleAverage(airYardsShares),

            CarryShare3Wk = UsageMetricsCalculator.WeightedAverage(carryShares, 3),
            CarryShare5Wk = UsageMetricsCalculator.WeightedAverage(carryShares, 5),
            CarryShareSeason = UsageMetricsCalculator.SimpleAverage(carryShares),

            Wopr3Wk = UsageMetricsCalculator.WeightedAverage(woprs, 3),
            WoprSeason = UsageMetricsCalculator.SimpleAverage(woprs),

            WeeksPlayed = activeLogs.Count,
            LastWeekProcessed = activeLogs.Last().Week,
            LastUpdated = DateTime.UtcNow
        };

        await _metricsRepository.UpsertAsync(metrics, ct);

        _logger.LogInformation(
            "Aggregated usage metrics for {PlayerId} season {Season} — {Weeks} weeks",
            playerId, season, activeLogs.Count);
    }

    public async Task AggregateAllPlayersAsync(
        int season,
        CancellationToken ct = default)
    {
        var playerIds = await _gameLogRepository
            .GetDistinctPlayerIdsAsync(season, ct);

        _logger.LogInformation(
            "Starting usage aggregation for {Count} players — season {Season}",
            playerIds.Count, season);

        foreach (var batch in playerIds.Chunk(10))
        {
            await Task.WhenAll(
                batch.Select(id => AggregatePlayerMetricsAsync(id, season, ct)));
        }

        _logger.LogInformation(
            "Completed usage aggregation for season {Season}", season);
    }
}
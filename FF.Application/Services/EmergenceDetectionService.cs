// FF.Application/Services/EmergenceDetectionService.cs
using FF.Application.Features.EmergenceAlert.Commands;
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Services;

public class EmergenceDetectionService(
    IPlayerUsageMetricsRepository usageMetricsRepository,
    IEmergenceAlertRepository alertRepository)
    : IRequestHandler<DetectEmergenceCommand, DetectEmergenceResult>
{
    private const decimal SnapSurgeThreshold = 0.18m;
    private const decimal TargetSurgeThreshold = 0.08m;
    private const decimal CarrySurgeThreshold = 0.15m;
    private const decimal WoprSpikeThreshold = 0.12m;

    private static readonly HashSet<string> SkillPositions =
        new(StringComparer.OrdinalIgnoreCase) { "QB", "RB", "WR", "TE" };

    public async Task<DetectEmergenceResult> Handle(
        DetectEmergenceCommand request,
        CancellationToken cancellationToken)
    {
        var allMetrics = await usageMetricsRepository
            .GetBySeasonAsync(request.Season, ct: cancellationToken);

        var skillMetrics = allMetrics
            .Where(m => SkillPositions.Contains(m.Position))
            .ToList();

        var alerts = new List<EmergenceAlertDocument>();

        foreach (var metrics in skillMetrics)
            alerts.AddRange(EvaluatePlayer(metrics, request.Season, request.Week));

        if (alerts.Count > 0)
            await alertRepository.UpsertBatchAsync(alerts, cancellationToken);

        return new DetectEmergenceResult(skillMetrics.Count, alerts.Count);
    }

    private static List<EmergenceAlertDocument> EvaluatePlayer(
        PlayerUsageMetricsDocument metrics, int season, int week)
    {
        var alerts = new List<EmergenceAlertDocument>();
        var now = DateTime.UtcNow;

        void TryAdd(EmergenceTriggerSignal signal, decimal recent, decimal seasonAvg, decimal threshold)
        {
            var delta = recent - seasonAvg;
            if (delta >= threshold)
            {
                alerts.Add(new EmergenceAlertDocument
                {
                    PlayerId = metrics.PlayerId,
                    PlayerName = metrics.PlayerName,
                    Position = metrics.Position,
                    NflTeam = string.IsNullOrEmpty(metrics.NflTeam) ? null : metrics.NflTeam,
                    TriggerSignal = signal,
                    Delta = Math.Round(delta, 4),
                    Week = week,
                    Season = season,
                    DetectedAt = now,
                    IsAcknowledged = false
                });
            }
        }

        TryAdd(EmergenceTriggerSignal.SnapShareSurge,
            metrics.SnapPct3Wk, metrics.SnapPctSeason, SnapSurgeThreshold);

        TryAdd(EmergenceTriggerSignal.TargetShareSurge,
            metrics.TargetShare3Wk, metrics.TargetShareSeason, TargetSurgeThreshold);

        if (metrics.Position.Equals("RB", StringComparison.OrdinalIgnoreCase))
            TryAdd(EmergenceTriggerSignal.CarryShareSurge,
                metrics.CarryShare3Wk, metrics.CarryShareSeason, CarrySurgeThreshold);

        if (metrics.Position.Equals("WR", StringComparison.OrdinalIgnoreCase) ||
            metrics.Position.Equals("TE", StringComparison.OrdinalIgnoreCase))
            TryAdd(EmergenceTriggerSignal.WoprSpike,
                metrics.Wopr3Wk, metrics.WoprSeason, WoprSpikeThreshold);

        return alerts;
    }
}
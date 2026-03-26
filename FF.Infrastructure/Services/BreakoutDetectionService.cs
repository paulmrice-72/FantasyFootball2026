using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Domain.Enums;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace FF.Infrastructure.Services;

public class BreakoutDetectionService(
    IPlayerRepository playerRepository,
    IPlayerUsageMetricsRepository usageMetricsRepository,
    ICareerSimulationRepository careerSimRepository,
    ILogger<BreakoutDetectionService> logger) : IBreakoutDetectionService
{
    private static readonly string[] ModelledPositions = ["QB", "RB", "WR", "TE"];

    public async Task<List<DynastyValuationDocument>> ScoreAllPlayersAsync(
        int season, CancellationToken ct = default)
    {
        var results = new List<DynastyValuationDocument>();

        foreach (var posStr in ModelledPositions)
        {
            var posEnum = Enum.Parse<Position>(posStr);
            var players = await playerRepository.GetByPositionAsync(posEnum, ct);
            var metrics = await usageMetricsRepository.GetBySeasonAsync(season, posStr, ct);
            var metricsMap = metrics.ToDictionary(m => m.PlayerId, m => m);

            foreach (var player in players)
            {
                if (player.SleeperPlayerId is null) continue;
                if (!player.Age.HasValue) continue;

                PlayerUsageMetricsDocument? usage = null;
                if (player.GsisId is not null)
                    metricsMap.TryGetValue(player.GsisId, out usage);

                CareerSimulationDocument? careerSim = await careerSimRepository
                    .GetByPlayerIdAsync(player.SleeperPlayerId, ct);

                var scoreResult = ScorePlayer(player, usage, careerSim);

                results.Add(new DynastyValuationDocument
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    SleeperPlayerId = player.SleeperPlayerId,
                    PlayerId = player.GsisId ?? string.Empty,
                    PlayerName = player.FullName,
                    Position = posStr,
                    NflTeam = player.NflTeam ?? string.Empty,
                    Age = player.Age.Value,
                    YearsExperience = player.YearsExperience,
                    Season = season,
                    BreakoutScore = scoreResult.Score,
                    BreakoutClassification = scoreResult.Classification,
                    BreakoutSignals = scoreResult.Signals,
                    BreakoutScoredAt = DateTime.UtcNow,
                    CareerValueScore = careerSim?.CareerValueScore ?? 0,
                    PeakYear = careerSim?.PeakYear ?? 0,
                    YearsOfPrimeRemaining = careerSim?.YearsOfPrimeRemaining ?? 0,
                    CareerPhase = careerSim?.CareerPhase ?? CareerPhase.Unknown
                });
            }
        }

        logger.LogInformation("Breakout detection complete — {Count} players scored", results.Count);
        return results;
    }

    public BreakoutScoreResult ScorePlayer(
        Domain.Entities.Player player,
        PlayerUsageMetricsDocument? metrics,
        CareerSimulationDocument? careerSim)
    {
        if (!player.Age.HasValue)
            return new BreakoutScoreResult(0, BreakoutClassification.Unknown, []);

        var signals = new List<string>();
        var pos = player.Position.ToString();
        double score = 0;

        // ── Signal 1: Age vs position peak (0-25 pts) ────────────────────
        var peakAge = GetPeakAge(pos);
        var ageToGo = peakAge - player.Age.Value;
        var ageScore = ageToGo switch
        {
            >= 3 and <= 5 => 25.0,
            >= 1 and < 3 => 20.0,
            0 => 15.0,
            < 0 and >= -2 => 8.0,
            _ => 2.0
        };
        score += ageScore;
        if (ageToGo is >= 1 and <= 5) signals.Add($"Age {player.Age.Value} — {ageToGo}yr to peak");

        // ── Signal 2: Years experience sweet spot (0-20 pts) ─────────────
        var exp = player.YearsExperience ?? 0;
        var expScore = exp switch
        {
            2 or 3 => 20.0,
            4 => 15.0,
            1 => 10.0,
            5 => 8.0,
            _ => 2.0
        };
        score += expScore;
        if (exp is 2 or 3) signals.Add($"Year {exp + 1} — prime breakout window");

        if (metrics is null)
        {
            var classification = ClassifyByScore(score, hasMetrics: false);
            return new BreakoutScoreResult(Math.Round(score, 1), classification, signals);
        }

        // ── Signal 3: Usage trend (0-20 pts) ─────────────────────────────
        var usageTrend = GetUsageTrend(metrics, pos);
        if (usageTrend > 0.03m)
        {
            score += 20.0;
            signals.Add($"Usage rising +{usageTrend:P0} (3wk vs season)");
        }
        else if (usageTrend > 0.01m)
        {
            score += 10.0;
            signals.Add("Usage trending up");
        }
        else if (usageTrend < -0.03m)
        {
            score -= 10.0;
            signals.Add("Usage declining");
        }

        // ── Signal 4: Snap % trend (0-15 pts) ────────────────────────────
        var snapTrend = metrics.SnapPct3Wk - metrics.SnapPctSeason;
        if (snapTrend > 0.05m)
        {
            score += 15.0;
            signals.Add($"Snap% expanding +{snapTrend:P0}");
        }
        else if (snapTrend > 0.02m)
        {
            score += 8.0;
        }
        else if (snapTrend < -0.05m)
        {
            score -= 8.0;
            signals.Add("Snap% declining");
        }

        // ── Signal 5: WOPR trend (0-10 pts) — WR/TE only ─────────────────
        if (pos is "WR" or "TE")
        {
            var woprTrend = metrics.Wopr3Wk - metrics.WoprSeason;
            if (woprTrend > 0.05m)
            {
                score += 10.0;
                signals.Add($"WOPR surging +{woprTrend:F2}");
            }
            else if (woprTrend > 0.02m)
            {
                score += 5.0;
            }
        }

        // ── Signal 6: aDOT rising (0-10 pts) — WR only ───────────────────
        if (pos == "WR")
        {
            var adotTrend = metrics.ADot3Wk - metrics.ADotSeason;
            if (adotTrend > 1.5m)
            {
                score += 10.0;
                signals.Add($"aDOT expanding +{adotTrend:F1}yds");
            }
            else if (adotTrend > 0.5m)
            {
                score += 5.0;
            }
        }

        score = Math.Max(0, Math.Min(100, score));
        return new BreakoutScoreResult(
            Math.Round(score, 1),
            ClassifyByScore(score, hasMetrics: true),
            signals);
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private static decimal GetUsageTrend(PlayerUsageMetricsDocument m, string position) =>
        position switch
        {
            "RB" => m.CarryShare3Wk - m.CarryShareSeason,
            "WR" => m.TargetShare3Wk - m.TargetShareSeason,
            "TE" => m.TargetShare3Wk - m.TargetShareSeason,
            "QB" => m.SnapPct3Wk - m.SnapPctSeason,
            _ => 0m
        };

    private static BreakoutClassification ClassifyByScore(double score, bool hasMetrics) =>
        score switch
        {
            >= 65 => BreakoutClassification.Breakout,
            >= 40 => BreakoutClassification.OnCurve,
            >= 20 when hasMetrics => BreakoutClassification.Declining,
            _ => BreakoutClassification.Unknown
        };

    private static int GetPeakAge(string position) => position switch
    {
        "QB" => 29,
        "RB" => 24,
        "WR" => 26,
        "TE" => 27,
        _ => 26
    };
}
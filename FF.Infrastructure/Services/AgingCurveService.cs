using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Domain.Enums;
using MathNet.Numerics;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace FF.Infrastructure.Services;

public class AgingCurveService(
    IPlayerGameLogRepository gameLogRepository,
    IPlayerRepository playerRepository,
    ILogger<AgingCurveService> logger) : IAgingCurveService
{
    private static readonly string[] ModelledPositions = ["QB", "RB", "WR", "TE"];

    private static readonly Dictionary<string, (int Min, int Max, int PeakAge)> AgeWindows = new()
    {
        ["QB"] = (22, 40, 29),
        ["RB"] = (21, 32, 24),
        ["WR"] = (21, 35, 26),
        ["TE"] = (21, 35, 27)
    };

    private static readonly int CurrentYear = 2026;

    public async Task<List<AgingCurveDocument>> BuildAllCurvesAsync(CancellationToken ct = default)
    {
        // Build SleeperPlayerId → Age lookup from SQL Player table (one DB hit)
        var ageMap = await BuildAgeMapAsync(ct);
        logger.LogInformation("Age map built — {Count} players with known age", ageMap.Count);

        var curves = new List<AgingCurveDocument>();

        foreach (var position in ModelledPositions)
        {
            var curve = await BuildCurveForPositionAsync(position, ageMap, ct);
            curves.Add(curve);
        }

        return curves;
    }

    public async Task<double> GetAgeMultiplierAsync(
        string position, int age, CancellationToken ct = default)
    {
        var window = AgeWindows.GetValueOrDefault(position.ToUpper(),
            (Min: 21, Max: 35, PeakAge: 26));
        return GetDefaultMultiplier(age, window.PeakAge, window.Min, window.Max);
    }

    public double EvaluateAtAge(AgingCurveDocument curve, int age)
    {
        if (curve.AgeValueMap.TryGetValue(age, out var val))
            return val;

        // Extrapolate via polynomial if age outside stored map
        if (curve.Coefficients.Length == 4)
        {
            double result = curve.Coefficients[0]
                          + curve.Coefficients[1] * age
                          + curve.Coefficients[2] * Math.Pow(age, 2)
                          + curve.Coefficients[3] * Math.Pow(age, 3);
            return Math.Max(0, Math.Min(100, result));
        }

        // Fallback — use default multiplier
        var window = AgeWindows.GetValueOrDefault(curve.Position,
            (Min: 21, Max: 35, PeakAge: 26));
        return GetDefaultMultiplier(age, window.PeakAge, window.Min, window.Max) * 100.0;
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task<Dictionary<string, int>> BuildAgeMapAsync(CancellationToken ct)
    {
        // Fetch all skill position players from SQL — one query per position
        var ageMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var posEnum in new[] { Position.QB, Position.RB, Position.WR, Position.TE })
        {
            var players = await playerRepository.GetByPositionAsync(posEnum, ct);
            foreach (var p in players)
            {
                if (p.SleeperPlayerId is not null && p.Age.HasValue)
                    ageMap[p.SleeperPlayerId] = p.Age.Value;
            }
        }

        return ageMap;
    }

    private async Task<AgingCurveDocument> BuildCurveForPositionAsync(
        string position,
        Dictionary<string, int> ageMap,
        CancellationToken ct)
    {
        var window = AgeWindows[position];
        var logs = await gameLogRepository.GetByPositionAsync(position, ct);

        if (logs.Count < 50)
        {
            logger.LogWarning(
                "{Position}: only {Count} logs — using default curve", position, logs.Count);
            return BuildDefaultCurve(position);
        }

        // Build age → list of FPPG values
        // Age at time of game = currentAge - (CurrentYear - season)
        var ageGroups = new Dictionary<int, List<double>>();

        foreach (var log in logs)
        {
            // Must have a SleeperPlayerId to look up age
            if (log.SleeperPlayerId is null) continue;
            if (!ageMap.TryGetValue(log.SleeperPlayerId, out var currentAge)) continue;

            var ageAtGame = currentAge - (CurrentYear - log.Season);

            if (ageAtGame < window.Min || ageAtGame > window.Max) continue;

            var fppg = ComputeFantasyPointsPpr(log);
            if (fppg <= 0) continue;  // skip DNP / inactive weeks

            if (!ageGroups.TryGetValue(ageAtGame, out var list))
            {
                list = [];
                ageGroups[ageAtGame] = list;
            }
            list.Add(fppg);
        }

        // Filter to age buckets with meaningful sample (≥10 game-weeks)
        var filteredGroups = ageGroups
            .Where(kv => kv.Value.Count >= 10)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Average());

        if (filteredGroups.Count < 4)
        {
            logger.LogWarning(
                "{Position}: only {Count} age buckets after filtering — using default curve",
                position, filteredGroups.Count);
            return BuildDefaultCurve(position);
        }

        // Normalize avg FPPG to 0-100 scale
        var maxFppg = filteredGroups.Values.Max();
        if (maxFppg <= 0) return BuildDefaultCurve(position);

        var ages = filteredGroups.Keys.OrderBy(a => a).Select(a => (double)a).ToArray();
        var normalized = ages.Select(a => filteredGroups[(int)a] / maxFppg * 100.0).ToArray();

        // Fit degree-3 polynomial — captures ascent, peak, and decline
        double[] coefficients;
        try
        {
            coefficients = Fit.Polynomial(ages, normalized, 3);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Polynomial fit failed for {Position}", position);
            return BuildDefaultCurve(position);
        }

        // Evaluate polynomial across full age window → AgeValueMap
        var ageValueMap = new Dictionary<int, double>();
        for (int age = window.Min; age <= window.Max; age++)
        {
            double val = coefficients[0]
                       + coefficients[1] * age
                       + coefficients[2] * Math.Pow(age, 2)
                       + coefficients[3] * Math.Pow(age, 3);
            ageValueMap[age] = Math.Max(0, Math.Min(100, val));
        }

        var peakAge = ageValueMap.MaxBy(kv => kv.Value).Key;
        var peakValue = ageValueMap.Values.Max();

        logger.LogInformation(
            "{Position} curve built — peak age {PeakAge}, {Buckets} age buckets, {Logs} logs",
            position, peakAge, filteredGroups.Count, logs.Count);

        return new AgingCurveDocument
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Position = position,
            Coefficients = coefficients,
            PeakAge = peakAge,
            PeakValue = peakValue,
            MinAge = window.Min,
            MaxAge = window.Max,
            AgeValueMap = ageValueMap,
            ComputedAt = DateTime.UtcNow,
            SampleSize = logs.Count,
            IsDefaultCurve = false
        };
    }

    private static double ComputeFantasyPointsPpr(PlayerGameLogDocument log)
    {
        // Use stored PPR value if available — most reliable
        if (log.FantasyPointsPpr.HasValue && log.FantasyPointsPpr.Value > 0)
            return (double)log.FantasyPointsPpr.Value;

        // Compute from raw stats as fallback
        return (double)(
            log.PassingYards / 25m
          + log.PassingTds * 4m
          - log.Interceptions * 2m
          + log.RushingYards / 10m
          + log.RushingTds * 6m
          + log.Receptions * 1m
          + log.ReceivingYards / 10m
          + log.ReceivingTds * 6m);
    }

    private static AgingCurveDocument BuildDefaultCurve(string position)
    {
        var window = AgeWindows.GetValueOrDefault(position,
            (Min: 21, Max: 35, PeakAge: 26));

        var ageValueMap = new Dictionary<int, double>();
        for (int age = window.Min; age <= window.Max; age++)
            ageValueMap[age] = GetDefaultMultiplier(age, window.PeakAge, window.Min, window.Max) * 100.0;

        return new AgingCurveDocument
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Position = position,
            Coefficients = [],
            PeakAge = window.PeakAge,
            PeakValue = 100.0,
            MinAge = window.Min,
            MaxAge = window.Max,
            AgeValueMap = ageValueMap,
            ComputedAt = DateTime.UtcNow,
            SampleSize = 0,
            IsDefaultCurve = true
        };
    }

    private static double GetDefaultMultiplier(int age, int peakAge, int minAge, int maxAge)
    {
        // Asymmetric bell — steeper post-peak decline than pre-peak ascent
        if (age <= peakAge)
        {
            double ascent = (double)(age - minAge) / (peakAge - minAge);
            return 0.6 + 0.4 * ascent;
        }
        else
        {
            double descent = (double)(age - peakAge) / (maxAge - peakAge);
            return Math.Max(0.1, 1.0 - 0.9 * descent * descent);
        }
    }
}
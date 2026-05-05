using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Application.Interfaces.Services;
using FF.Domain.Documents;
using FF.Domain.Enums;
using MathNet.Numerics.Distributions;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;

namespace FF.Infrastructure.Services;

public class CareerSimulationService(
    IPlayerRepository playerRepository,
    IAgingCurveRepository agingCurveRepository,
    ISimulationResultRepository simulationResultRepository,
    ILogger<CareerSimulationService> logger) : ICareerSimulationService
{
    private const int Iterations = 1000;
    private const int ProjectYears = 5;
    private const int CurrentSeason = 2026;

    private static readonly Dictionary<string, double> BaseInjuryRisk = new()
    {
        ["QB"] = 0.12,
        ["RB"] = 0.22,
        ["WR"] = 0.15,
        ["TE"] = 0.14
    };

    private static readonly Dictionary<string, double> AgeInjuryIncrement = new()
    {
        ["QB"] = 0.015,
        ["RB"] = 0.030,
        ["WR"] = 0.018,
        ["TE"] = 0.020
    };

    private static readonly Dictionary<string, int> PeakAges = new()
    {
        ["QB"] = 29,
        ["RB"] = 24,
        ["WR"] = 26,
        ["TE"] = 27
    };

    private static readonly Dictionary<string, double> StarterThreshold = new()
    {
        ["QB"] = 16.0,
        ["RB"] = 7.0,
        ["WR"] = 7.5,
        ["TE"] = 6.0
    };

    public async Task<List<CareerSimulationDocument>> SimulateAllPlayersAsync(
        int season,
        CancellationToken ct = default)
    {
        var results = new List<CareerSimulationDocument>();
        var positions = new[] { Position.QB, Position.RB, Position.WR, Position.TE };

        // ── Bulk-load aging curves ─────────────────────────────────────────
        var curves = new Dictionary<string, AgingCurveDocument?>();
        foreach (var pos in new[] { "QB", "RB", "WR", "TE" })
            curves[pos] = await agingCurveRepository.GetByPositionAsync(pos, ct);

        // ── Bulk-load ALL season-average sim results in ONE query ──────────
        // Replaces per-player serial DB calls in GetBaselineFppgAsync.
        // With ~700 players × up to 3 season fallbacks = up to 2,100 queries
        // eliminated and replaced with 1 query + in-memory lookups.
        var allSimResults = await simulationResultRepository.GetAllSeasonAveragesAsync(ct);

        logger.LogInformation(
            "Bulk-loaded {Count} season-average sim results for baseline lookup",
            allSimResults.Count);

        // Primary lookup: SleeperPlayerId → best (most recent season) result
        var simByPlayerId = allSimResults
            .Where(r => !string.IsNullOrEmpty(r.SleeperPlayerId) && r.Median > 0)
            .GroupBy(r => r.SleeperPlayerId!)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.Season).First());

        // Fallback lookup: "PlayerName|Position" → best result
        var simByNamePos = allSimResults
            .Where(r => !string.IsNullOrEmpty(r.PlayerName) && r.Median > 0)
            .GroupBy(r => $"{r.PlayerName}|{r.Position}")
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(r => r.Season).First());

        // ── Simulate each player ───────────────────────────────────────────
        foreach (var position in positions)
        {
            var players = await playerRepository.GetByPositionAsync(position, ct);
            var posStr = position.ToString();

            foreach (var player in players)
            {
                if (player.SleeperPlayerId is null) continue;
                if (!player.Age.HasValue && player.YearsExperience != 0) continue;
                if (player.Age.HasValue && player.Age.Value < 18) continue;

                try
                {
                    var sim = SimulatePlayer(
                        player,
                        posStr,
                        curves[posStr],
                        season,
                        simByPlayerId,
                        simByNamePos);

                    results.Add(sim);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Career sim failed for {Player}", player.FullName);
                }
            }
        }

        logger.LogInformation("Career simulations complete — {Count} players", results.Count);
        return results;
    }

    public async Task<CareerSimulationDocument> SimulatePlayerCareerAsync(
        string sleeperPlayerId,
        CancellationToken ct = default)
    {
        var player = await playerRepository.GetBySleeperIdAsync(sleeperPlayerId, ct)
            ?? throw new InvalidOperationException($"Player not found: {sleeperPlayerId}");

        var posStr = player.Position.ToString();
        var curve = await agingCurveRepository.GetByPositionAsync(posStr, ct);

        // Single-player path still uses targeted queries (infrequent, acceptable)
        var allSimResults = await simulationResultRepository.GetAllSeasonAveragesAsync(ct);

        var simByPlayerId = allSimResults
            .Where(r => !string.IsNullOrEmpty(r.SleeperPlayerId) && r.Median > 0)
            .GroupBy(r => r.SleeperPlayerId!)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Season).First());

        var simByNamePos = allSimResults
            .Where(r => !string.IsNullOrEmpty(r.PlayerName) && r.Median > 0)
            .GroupBy(r => $"{r.PlayerName}|{r.Position}")
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.Season).First());

        return SimulatePlayer(player, posStr, curve, CurrentSeason, simByPlayerId, simByNamePos);
    }

    // ── Private ────────────────────────────────────────────────────────────────

    private CareerSimulationDocument SimulatePlayer(
        FF.Domain.Entities.Player player,
        string position,
        AgingCurveDocument? curve,
        int season,
        Dictionary<string, SimulationResultDocument> simByPlayerId,
        Dictionary<string, SimulationResultDocument> simByNamePos)
    {
        var currentAge = player.Age ?? (player.YearsExperience == 0 ? 21 : 22);

        var simBaseline = GetBaselineFppg(
            player.SleeperPlayerId!,
            player.FullName,
            position,
            simByPlayerId,
            simByNamePos);

        var baseFppg = GetBaselineFppgWithContext(position, simBaseline, player);
        if (baseFppg <= 0) baseFppg = GetDepthLevelFppg(position);

        var yearProjections = new List<CareerYearProjection>();
        var rng = new Random();

        for (int yearOffset = 0; yearOffset < ProjectYears; yearOffset++)
        {
            var projYear = season + yearOffset;
            var ageAtYear = currentAge + yearOffset;
            var aging = GetAgingMultiplier(curve, position, ageAtYear);
            var injury = GetInjuryRisk(position, ageAtYear);

            var yearSamples = new double[Iterations];
            var stdDev = baseFppg * aging * GetPositionVariance(position);

            for (int i = 0; i < Iterations; i++)
            {
                var projected = Normal.Sample(rng, baseFppg * aging, stdDev);
                var injuryRoll = rng.NextDouble();
                var gamesPlayed = injuryRoll < injury
                    ? 17.0 * (1.0 - injury * 0.6)
                    : 17.0;
                yearSamples[i] = Math.Max(0, projected * (gamesPlayed / 17.0));
            }

            Array.Sort(yearSamples);
            var median = yearSamples[Iterations / 2];
            var floor = yearSamples[(int)(Iterations * 0.10)];
            var ceiling = yearSamples[(int)(Iterations * 0.90)];

            yearProjections.Add(new CareerYearProjection
            {
                Year = projYear,
                AgeAtYear = ageAtYear,
                AgingMultiplier = aging,
                MedianFppg = Math.Round(median, 2),
                FloorFppg = Math.Round(floor, 2),
                CeilingFppg = Math.Round(ceiling, 2),
                InjuryRisk = Math.Round(injury, 3),
                ExpectedGamesPlayed = Math.Round(17.0 * (1.0 - injury), 1),
                SeasonValue = Math.Round(median * 17.0 * (1.0 - injury), 1),
                Phase = ClassifyPhase(position, ageAtYear)
            });
        }

        var careerValue = yearProjections
            .Select((y, i) => y.SeasonValue / Math.Pow(1.15, i))
            .Sum();

        var peakYear = yearProjections.MaxBy(y => y.SeasonValue)!;
        var primeYears = yearProjections.Count(y => y.AgingMultiplier >= 0.70);

        return new CareerSimulationDocument
        {
            Id = ObjectId.GenerateNewId().ToString(),
            SleeperPlayerId = player.SleeperPlayerId!,
            PlayerName = player.FullName,
            Position = position,
            CurrentAge = currentAge,
            Season = season,
            CareerPhase = ClassifyPhase(position, currentAge),
            YearProjections = yearProjections,
            CareerValueScore = Math.Round(careerValue, 1),
            PeakYearValue = peakYear.SeasonValue,
            PeakYear = peakYear.Year,
            YearsOfPrimeRemaining = primeYears,
            ComputedAt = DateTime.UtcNow,
            Iterations = Iterations
        };
    }

    /// <summary>
    /// Pure in-memory baseline lookup — no DB calls.
    /// Uses pre-loaded dictionaries built once at the start of SimulateAllPlayersAsync.
    /// </summary>
    private double GetBaselineFppg(
        string sleeperPlayerId,
        string playerName,
        string position,
        Dictionary<string, SimulationResultDocument> simByPlayerId,
        Dictionary<string, SimulationResultDocument> simByNamePos)
    {
        SimulationResultDocument? sim = null;

        if (simByPlayerId.TryGetValue(sleeperPlayerId, out var byId) && byId.Median > 0)
            sim = byId;
        else
        {
            var key = $"{playerName}|{position}";
            if (simByNamePos.TryGetValue(key, out var byName) && byName.Median > 0)
            {
                logger.LogDebug(
                    "Used name fallback for {Player} — SleeperPlayerId {Id} had no sim result",
                    playerName, sleeperPlayerId);
                sim = byName;
            }
        }

        if (sim is null) return 0;

        var baseline = (double)sim.Median;

        // Availability discount — high stdDev/median ratio signals injury-prone
        // or limited-sample players whose baseline overstates true dynasty value.
        // Threshold 0.19: captures Tua (0.20), McCaffrey-type injury histories
        // without penalizing normal variance (Allen ~0.15, Hurts ~0.16).
        if (position == "QB" && sim.StandardDeviation > 0 && sim.Median > 0)
        {
            var volatility = (double)sim.StandardDeviation / (double)sim.Median;
            if (volatility > 0.19)
            {
                baseline *= 0.82; // ~18% discount — moves Tua from 16.5 → 13.5
                logger.LogDebug(
                    "Volatility discount applied to {Player} — ratio {Ratio:F2}",
                    playerName, volatility);
            }
        }

        return baseline;
    }

    private static double GetBaselineFppgWithContext(
        string position,
        double simulationBaseline,
        FF.Domain.Entities.Player player)
    {
        if (player.YearsExperience == 0)
        {
            if (simulationBaseline > 0) return simulationBaseline;
            if (position == "QB")
            {
                var pick = player.DraftPick ?? 999;
                var round = player.DraftRound ?? 99;
                return (round == 1 && pick <= 5)
                    ? GetStarterAverageFppg(position)
                    : GetDepthLevelFppg(position);
            }
            return GetStarterAverageFppg(position);
        }

        if (simulationBaseline > 0)
        {
            var threshold = StarterThreshold.GetValueOrDefault(position, 7.0);
            if (player.YearsExperience >= 1 && simulationBaseline < threshold)
                return GetDepthLevelFppg(position);

            if (position == "QB" && player.YearsExperience <= 3)
            {
                var credibilityCap = player.YearsExperience switch
                {
                    1 => 14.0,
                    2 => 16.0,
                    3 => 18.0,
                    _ => simulationBaseline
                };
                return Math.Min(simulationBaseline, credibilityCap);
            }

            // Veteran age regression — QBs 30+ should not project from a single
            // peak season. Cap at a declining-starter level to prevent career-year
            // outliers (Mayfield 2024, Goff 2024) from inflating dynasty value.
            if (position == "QB" && player.YearsExperience >= 7)
            {
                var ageCap = (player.Age ?? 30) switch
                {
                    >= 32 => 16.0,
                    31 => 18.0,
                    30 => 20.0,
                    _ => simulationBaseline
                };
                return Math.Min(simulationBaseline, ageCap);
            }

            return simulationBaseline;
        }

        return 0.1;
    }

    private static double GetStarterAverageFppg(string position) => position switch
    {
        "QB" => 18.0,
        "RB" => 9.0,
        "WR" => 10.0,
        "TE" => 8.5,
        _ => 9.0
    };

    private static double GetDepthLevelFppg(string position) => position switch
    {
        "QB" => 6.0,
        "RB" => 4.0,
        "WR" => 4.5,
        "TE" => 3.5,
        _ => 4.0
    };

    private static double GetAgingMultiplier(
        AgingCurveDocument? curve, string position, int age)
    {
        if (curve is null) return GetFallbackMultiplier(position, age);
        if (curve.AgeValueMap.TryGetValue(age, out var val)) return val / 100.0;
        return GetFallbackMultiplier(position, age);
    }

    private static double GetInjuryRisk(string position, int age)
    {
        var baseRisk = BaseInjuryRisk.GetValueOrDefault(position, 0.15);
        var peakAge = PeakAges.GetValueOrDefault(position, 26);
        var increment = AgeInjuryIncrement.GetValueOrDefault(position, 0.02);
        var yearsOver = Math.Max(0, age - peakAge);
        return Math.Min(0.65, baseRisk + yearsOver * increment);
    }

    private static CareerPhase ClassifyPhase(string position, int age)
    {
        var peak = PeakAges.GetValueOrDefault(position, 26);
        return age < peak - 2 ? CareerPhase.Ascending
            : age <= peak + 2 ? CareerPhase.Prime
            : age <= peak + 5 ? CareerPhase.Declining
            : CareerPhase.Unknown;
    }

    private static double GetPositionVariance(string position) => position switch
    {
        "QB" => 0.18,
        "RB" => 0.25,
        "WR" => 0.28,
        "TE" => 0.22,
        _ => 0.25
    };

    private static double GetFallbackMultiplier(string position, int age)
    {
        var peak = PeakAges.GetValueOrDefault(position, 26);
        if (age <= peak)
            return 0.6 + 0.4 * ((double)(age - 18) / (peak - 18));
        return Math.Max(0.1, 1.0 - 0.9 * Math.Pow((double)(age - peak) / 15.0, 2));
    }
}
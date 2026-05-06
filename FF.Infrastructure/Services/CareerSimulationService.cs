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

    // ── Empirical Bayes shrinkage ─────────────────────────────────────────────
    // credibility = min(YearsExp, 5) / (min(YearsExp, 5) + K)
    // blended     = credibility × raw + (1 - credibility) × prior
    // K=3: rookie → 0% credibility (full prior), 5yr vet → 62.5% (cap)
    // Admin-configurable in a future sprint (ADMIN-WEIGHT-001).
    private const double ShrinkageK = 3.0;

    private static readonly Dictionary<string, double> PositionPriors = new()
    {
        ["QB"] = 14.0,  // median starter, excludes top-tier inflation
        ["RB"] = 9.5,   // accounts for committee backs
        ["WR"] = 9.0,   // slot + role players drag median down
        ["TE"] = 7.5,   // heavy TE2 population
    };

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

    private static readonly Dictionary<string, double> PostPeakWindow = new()
    {
        ["QB"] = 8.0,
        ["RB"] = 5.0,
        ["WR"] = 9.0,
        ["TE"] = 8.0,
    };

    public async Task<List<CareerSimulationDocument>> SimulateAllPlayersAsync(
        int season, CancellationToken ct = default)
    {
        var results = new List<CareerSimulationDocument>();
        var positions = new[] { Position.QB, Position.RB, Position.WR, Position.TE };

        // ── Bulk-load aging curves ───────────────────────────────────────────
        var curves = new Dictionary<string, AgingCurveDocument?>();
        foreach (var pos in new[] { "QB", "RB", "WR", "TE" })
            curves[pos] = await agingCurveRepository.GetByPositionAsync(pos, ct);

        // ── Bulk-load ALL season-average sim results in ONE query ────────────
        var allSimResults = await simulationResultRepository.GetAllSeasonAveragesAsync(ct);
        logger.LogInformation(
            "Bulk-loaded {Count} season-average sim results for baseline lookup",
            allSimResults.Count);

        // Multi-season merge — average 2024+2025 where both exist.
        // Prevents one outlier season (Darnold 2024: 18.1) from seeding
        // an inflated 5-year projection. Uses best single season only as fallback.
        var simByPlayerId = allSimResults
            .Where(r => !string.IsNullOrEmpty(r.SleeperPlayerId) && r.Median > 0
                && r.Week == 0) // season-average sentinel only
            .GroupBy(r => r.SleeperPlayerId!)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var seasons = g.OrderByDescending(r => r.Season).ToList();
                    if (seasons.Count == 1) return seasons[0];
                    // Average the two most recent seasons
                    var recent = seasons[0];
                    var prior = seasons[1];
                    return new SimulationResultDocument
                    {
                        SleeperPlayerId = recent.SleeperPlayerId,
                        PlayerName = recent.PlayerName,
                        Position = recent.Position,
                        NflTeam = recent.NflTeam,
                        Season = recent.Season,
                        Week = 0,
                        Median = Math.Round((recent.Median + prior.Median) / 2, 2),
                        Floor = Math.Round((recent.Floor + prior.Floor) / 2, 2),
                        Ceiling = Math.Round((recent.Ceiling + prior.Ceiling) / 2, 2),
                        Mean = Math.Round((recent.Mean + prior.Mean) / 2, 2),
                        BaseProjection = Math.Round((recent.BaseProjection + prior.BaseProjection) / 2, 2),
                        StandardDeviation = recent.StandardDeviation,
                        ScoringFormat = recent.ScoringFormat,
                        CalculatedAt = DateTime.UtcNow,
                        PlayerRole = "SeasonAverage"
                    };
                });

        var simByNamePos = allSimResults
            .Where(r => !string.IsNullOrEmpty(r.PlayerName) && r.Median > 0
                && r.Week == 0)
            .GroupBy(r => $"{r.PlayerName}|{r.Position}")
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var seasons = g.OrderByDescending(r => r.Season).ToList();
                    if (seasons.Count == 1) return seasons[0];
                    var recent = seasons[0];
                    var prior = seasons[1];
                    return new SimulationResultDocument
                    {
                        SleeperPlayerId = recent.SleeperPlayerId,
                        PlayerName = recent.PlayerName,
                        Position = recent.Position,
                        Season = recent.Season,
                        Week = 0,
                        Median = Math.Round((recent.Median + prior.Median) / 2, 2),
                        Floor = Math.Round((recent.Floor + prior.Floor) / 2, 2),
                        Ceiling = Math.Round((recent.Ceiling + prior.Ceiling) / 2, 2),
                        Mean = Math.Round((recent.Mean + prior.Mean) / 2, 2),
                        BaseProjection = Math.Round((recent.BaseProjection + prior.BaseProjection) / 2, 2),
                        StandardDeviation = recent.StandardDeviation,
                        ScoringFormat = recent.ScoringFormat,
                        CalculatedAt = DateTime.UtcNow,
                        PlayerRole = "SeasonAverage"
                    };
                });

        // ── Simulate each player ─────────────────────────────────────────────
        foreach (var position in positions)
        {
            var players = (await playerRepository.GetByPositionAsync(position, ct))
                .GroupBy(p => p.SleeperPlayerId)
                .Select(g => g.First())
                .ToList();
            var posStr = position.ToString();

            foreach (var player in players)
            {
                if (player.SleeperPlayerId is null) continue;
                if (!player.Age.HasValue && player.YearsExperience != 0) continue;
                if (player.Age.HasValue && player.Age.Value < 18) continue;

                try
                {
                    var sim = SimulatePlayer(
                        player, posStr, curves[posStr], season,
                        simByPlayerId, simByNamePos);
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
        string sleeperPlayerId, CancellationToken ct = default)
    {
        var player = await playerRepository.GetBySleeperIdAsync(sleeperPlayerId, ct)
            ?? throw new InvalidOperationException($"Player not found: {sleeperPlayerId}");

        var posStr = player.Position.ToString();
        var curve = await agingCurveRepository.GetByPositionAsync(posStr, ct);

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

    // ── Private ───────────────────────────────────────────────────────────────

    private CareerSimulationDocument SimulatePlayer(
        FF.Domain.Entities.Player player,
        string position,
        AgingCurveDocument? curve,
        int season,
        Dictionary<string, SimulationResultDocument> simByPlayerId,
        Dictionary<string, SimulationResultDocument> simByNamePos)
    {
        var currentAge = player.Age ?? (player.YearsExperience == 0 ? 21 : 22);

        var rawBaseline = GetBaselineFppg(
            player.SleeperPlayerId!, player.FullName, position,
            simByPlayerId, simByNamePos);

        var baseFppg = ApplyShrinkage(position, rawBaseline, player);

        if (baseFppg <= 0)
            baseFppg = GetDepthLevelFppg(position);

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
    /// Pure in-memory baseline lookup — no DB calls. Volatility discount
    /// removed; shrinkage handles low-evidence players naturally.
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

        return (double)sim.Median;
    }

    /// <summary>
    /// Empirical Bayes shrinkage — blends raw FPPG toward position prior
    /// weighted by evidence (years of experience).
    ///
    /// credibility = min(YearsExp, 5) / (min(YearsExp, 5) + K)
    /// blended     = credibility × raw + (1 - credibility) × prior
    ///
    /// K=3:  0 yrs → 0% (full prior)  1 yr → 25%  3 yrs → 50%  5+ yrs → 62.5%
    ///
    /// Journeyman cap: age 28+, exp 8+ QBs capped at 21.0 FPPG blended.
    /// Catches Mayfield/Darnold/Goff without affecting Allen/Burrow/Hurts.
    ///
    /// Age regression multipliers apply in SimulatePlayer AFTER this returns.
    /// </summary>
    private static double ApplyShrinkage(
        string position,
        double rawFppg,
        FF.Domain.Entities.Player player)
    {
        var prior = PositionPriors.GetValueOrDefault(position, 9.0);
        var clampedExp = Math.Min(player.YearsExperience ?? 0, 5);
        var credibility = clampedExp / (clampedExp + ShrinkageK);

        // Rookie QB with no sim data and no draft pedigree → depth-level,
        // not prior. Prior (14.0) is still too high for unknown QBs —
        // it puts Beck/Green/Simpson in the top 20 via superflex inflation.
        if (position == "QB" && (player.YearsExperience ?? 0) == 0 && rawFppg <= 0)
        {
            var round = player.DraftRound ?? 99;
            var pick = player.DraftPick ?? 999;
            // Only top-5 picks earn the starter prior; everyone else is depth
            return (round == 1 && pick <= 5)
                ? prior          // 14.0 — high pick, legitimate prospect
                : GetDepthLevelFppg(position);  // 6.0 — unknown/late round
        }

        // Standard shrinkage blend
        var blended = rawFppg <= 0
            ? prior
            : credibility * rawFppg + (1.0 - credibility) * prior;

        // Journeyman QB cap — Mayfield (31/exp8), Darnold (28/exp8), Goff tier.
        // Allen age 29 exp 7, Burrow age 29 exp 6 — NOT caught.
        if (position == "QB"
            && (player.Age ?? 0) >= 28
            && (player.YearsExperience ?? 0) >= 8)
        {
            blended = Math.Min(blended, 21.0);
        }

        // Starter threshold gate — experienced depth players pulled UP toward
        // prior get floored to depth level instead.
        if ((player.YearsExperience ?? 0) >= 1)
        {
            var threshold = StarterThreshold.GetValueOrDefault(position, 7.0);
            if (blended < threshold)
                return GetDepthLevelFppg(position);
        }

        return blended;
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

        var window = PostPeakWindow.GetValueOrDefault(position, 8.0);
        return Math.Max(0.1, 1.0 - 0.9 * Math.Pow((double)(age - peak) / window, 2));
    }
}
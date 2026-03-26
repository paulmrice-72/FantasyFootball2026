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

    // Injury probability by position and age band
    // Probability of missing 4+ games in a season
    private static readonly Dictionary<string, double> BaseInjuryRisk = new()
    {
        ["QB"] = 0.12,
        ["RB"] = 0.22,
        ["WR"] = 0.15,
        ["TE"] = 0.14
    };

    // Injury risk increases with age — added per year over peak age
    private static readonly Dictionary<string, double> AgeInjuryIncrement = new()
    {
        ["QB"] = 0.015,
        ["RB"] = 0.030,   // RBs accumulate wear faster
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

    public async Task<List<CareerSimulationDocument>> SimulateAllPlayersAsync(
        int season, CancellationToken ct = default)
    {
        var results = new List<CareerSimulationDocument>();
        var positions = new[] { Position.QB, Position.RB, Position.WR, Position.TE };

        // Load all aging curves up front — one DB hit per position
        var curves = new Dictionary<string, AgingCurveDocument?>();
        foreach (var pos in new[] { "QB", "RB", "WR", "TE" })
            curves[pos] = await agingCurveRepository.GetByPositionAsync(pos, ct);

        foreach (var position in positions)
        {
            var players = await playerRepository.GetByPositionAsync(position, ct);
            var posStr = position.ToString();

            foreach (var player in players)
            {
                if (player.SleeperPlayerId is null) continue;
                if (!player.Age.HasValue || player.Age.Value < 18) continue;

                try
                {
                    var sim = await SimulatePlayerAsync(
                        player, posStr, curves[posStr], season, ct);
                    results.Add(sim);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "Career sim failed for {Player}", player.FullName);
                }
            }
        }

        logger.LogInformation("Career simulations complete — {Count} players", results.Count);
        return results;
    }

    public async Task<CareerSimulationDocument> SimulatePlayerCareerAsync(
        string sleeperPlayerId, CancellationToken ct = default)
    {
        var player = await playerRepository.GetBySleeperIdAsync(sleeperPlayerId, ct) ?? throw new InvalidOperationException($"Player not found: {sleeperPlayerId}");
        var posStr = player.Position.ToString();
        var curve = await agingCurveRepository.GetByPositionAsync(posStr, ct);

        return await SimulatePlayerAsync(player, posStr, curve, CurrentSeason, ct);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private async Task<CareerSimulationDocument> SimulatePlayerAsync(
        FF.Domain.Entities.Player player,
        string position,
        AgingCurveDocument? curve,
        int season,
        CancellationToken ct)
    {
        var currentAge = player.Age!.Value;

        // Get baseline FPPG from most recent simulation results
        var baseFppg = await GetBaselineFppgAsync(player.SleeperPlayerId!, season, ct);

        // If no simulation result exists, estimate from position average
        if (baseFppg <= 0)
            baseFppg = GetPositionAverageFppg(position);

        var yearProjections = new List<CareerYearProjection>();
        var rng = new Random();

        for (int yearOffset = 0; yearOffset < ProjectYears; yearOffset++)
        {
            var projYear = season + yearOffset;
            var ageAtYear = currentAge + yearOffset;
            var aging = GetAgingMultiplier(curve, position, ageAtYear);
            var injury = GetInjuryRisk(position, ageAtYear);

            // Monte Carlo — run Iterations samples for this year
            var yearSamples = new double[Iterations];
            var stdDev = baseFppg * aging * GetPositionVariance(position);

            for (int i = 0; i < Iterations; i++)
            {
                // Normal distribution centred on age-adjusted projection
                var projected = Normal.Sample(rng, baseFppg * aging, stdDev);
                var injuryRoll = rng.NextDouble();
                var gamesPlayed = injuryRoll < injury
                    ? 17.0 * (1.0 - injury * 0.6)   // partial season on injury
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

        // Career value = discounted sum of season values (earlier years worth more)
        var careerValue = yearProjections
            .Select((y, i) => y.SeasonValue / Math.Pow(1.15, i))
            .Sum();

        var peakYear = yearProjections.MaxBy(y => y.SeasonValue)!;

        // Years of prime = years where aging multiplier >= 0.70
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

    private async Task<double> GetBaselineFppgAsync(
        string sleeperPlayerId, int season, CancellationToken ct)
    {
        try
        {
            var result = await simulationResultRepository
                .GetMostRecentBySleeperIdAsync(sleeperPlayerId, season, ct);

            return result?.Median > 0 ? (double)result.Median : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static double GetAgingMultiplier(
        AgingCurveDocument? curve, string position, int age)
    {
        if (curve is null) return GetFallbackMultiplier(position, age);

        if (curve.AgeValueMap.TryGetValue(age, out var val))
            return val / 100.0;   // stored as 0-100, convert to 0-1

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

    private static double GetPositionAverageFppg(string position) => position switch
    {
        "QB" => 18.0,
        "RB" => 9.0,
        "WR" => 10.0,
        "TE" => 8.5,
        _ => 9.0
    };

    private static double GetFallbackMultiplier(string position, int age)
    {
        var peak = PeakAges.GetValueOrDefault(position, 26);
        if (age <= peak) return 0.6 + 0.4 * ((double)(age - 18) / (peak - 18));
        return Math.Max(0.1, 1.0 - 0.9 * Math.Pow((double)(age - peak) / 15.0, 2));
    }
}
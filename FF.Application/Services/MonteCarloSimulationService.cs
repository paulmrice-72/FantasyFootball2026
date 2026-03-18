// FF.Application/Services/MonteCarloSimulationService.cs
using FF.Domain.Documents;
using FF.Domain.Enums;

namespace FF.Application.Services;

/// <summary>
/// Runs Monte Carlo simulation over a player's projection to produce
/// a full performance distribution — floor, median, ceiling, boom/bust probabilities.
///
/// Variance model: position-specific standard deviation as a fraction of
/// the base projection. Deep threats and handcuffs have higher variance;
/// bell cows and slot receivers have lower variance.
/// </summary>
public static class MonteCarloSimulationService
{
    public const int DefaultIterations = 10_000;

    // Position-based variance coefficients (std dev as % of base projection)
    // Higher = more volatile, lower = more predictable
    private static readonly Dictionary<string, decimal> PositionVariance = new()
    {
        ["QB"] = 0.28m,
        ["RB"] = 0.38m,
        ["WR"] = 0.42m,
        ["TE"] = 0.40m,
    };

    // Role-based variance modifiers — applied on top of position variance
    private static readonly Dictionary<PlayerRole, decimal> RoleVarianceModifier = new()
    {
        [PlayerRole.WR1Alpha] = 0.90m,  // high volume = lower variance
        [PlayerRole.SlotPossession] = 0.85m,  // stable floor
        [PlayerRole.DeepThreat] = 1.30m,  // boom/bust
        [PlayerRole.BellCow] = 0.80m,  // workhorse = predictable
        [PlayerRole.PassCatcher] = 1.10m,  // PPR-dependent, slightly volatile
        [PlayerRole.Handcuff] = 1.50m,  // binary: starter or nothing
        [PlayerRole.SeamReceiver] = 0.95m,  // WR-like consistency
        [PlayerRole.BlockerSpot] = 1.40m,  // low floor, spike upside
        [PlayerRole.StartingQB] = 0.85m,  // consistent volume
        [PlayerRole.BackupQB] = 1.60m,  // binary playing time
        [PlayerRole.Unknown] = 1.00m,  // no modifier
    };

    public static SimulationResultDocument Simulate(
        PlayerProjectionDocument projection,
        PlayerRole role = PlayerRole.Unknown,
        int iterations = DefaultIterations,
        int? seed = null)
    {
        var baseProjection = projection.ProjectedPointsHalfPpr;

        // Derive standard deviation from position + role
        var positionCoeff = PositionVariance.GetValueOrDefault(projection.Position, 0.40m);
        var roleModifier = RoleVarianceModifier.GetValueOrDefault(role, 1.00m);
        var stdDev = baseProjection * positionCoeff * roleModifier;

        // Run iterations
        var rng = seed.HasValue ? new Random(seed.Value) : new Random();
        var results = new double[iterations];

        for (var i = 0; i < iterations; i++)
        {
            // Box-Muller transform — produces normally distributed random values
            var u1 = 1.0 - rng.NextDouble();
            var u2 = 1.0 - rng.NextDouble();
            var z = Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
            var value = (double)baseProjection + z * (double)stdDev;

            results[i] = Math.Max(0.0, value); // floor at zero
        }

        Array.Sort(results);

        var floor = (decimal)results[(int)(iterations * 0.10)];
        var median = (decimal)results[(int)(iterations * 0.50)];
        var ceiling = (decimal)results[(int)(iterations * 0.90)];
        var mean = (decimal)(results.Average());

        // Boom = score >= 2x base projection
        // Bust  = score <= 0.5x base projection
        var boomThreshold = (double)baseProjection * 2.0;
        var bustThreshold = (double)baseProjection * 0.5;

        var boomProbability = baseProjection > 0
            ? (decimal)results.Count(r => r >= boomThreshold) / iterations
            : 0m;

        var bustProbability = baseProjection > 0
            ? (decimal)results.Count(r => r <= bustThreshold) / iterations
            : 1m;

        return new SimulationResultDocument
        {
            PlayerId = projection.PlayerId,
            PlayerName = projection.PlayerName,
            Position = projection.Position,
            NflTeam = projection.NflTeam,
            OpponentTeam = projection.OpponentTeam,
            Season = projection.Season,
            Week = projection.Week,
            Iterations = iterations,
            BaseProjection = Math.Round(baseProjection, 2),
            StandardDeviation = Math.Round(stdDev, 2),
            Floor = Math.Round(floor, 2),
            Median = Math.Round(median, 2),
            Ceiling = Math.Round(ceiling, 2),
            Mean = Math.Round(mean, 2),
            BoomProbability = Math.Round(boomProbability, 4),
            BustProbability = Math.Round(bustProbability, 4),
            PlayerRole = role.ToString(),
            ScoringFormat = projection.ScoringFormat,
            CalculatedAt = DateTime.UtcNow
        };
    }
}
// FF.Application/Services/MonteCarloSimulationService.cs
using FF.Domain.Documents;
using FF.Domain.Enums;
using FF.Domain.ValueObjects;

namespace FF.Application.Services;

/// <summary>
/// L2 of the Unified Projection Engine (Epic 20 / FAN-117).
///
/// Simulates a player's performance distribution — floor, median, ceiling,
/// boom/bust. Where the old model jittered a single fantasy-point total with one
/// Gaussian, this simulates the STAT LINE and scores each iteration through L1.
///
/// Why that matters, beyond tidiness:
///
/// * **Shape.** Fantasy scoring is right-skewed — a compressed floor and a long
///   ceiling — because touchdowns are rare countable events. A Gaussian on total
///   points is symmetric by construction and literally cannot represent that. The
///   stat-line model produces a p90-to-p50 gap 1.3-1.5x the p50-to-p10 gap, which
///   is what real weekly scoring looks like.
/// * **Honesty about what varies.** Volume and efficiency are separate sources of
///   risk. A bell-cow back with poor efficiency is a different bet from a
///   committee back who breaks one long run; the old single coefficient could not
///   tell them apart.
/// * **Format independence.** Because each iteration is a stat line, the same
///   simulation can be scored in any league's format. Today it is scored full-PPR
///   to keep every existing reader working, but nothing about the model is
///   format-bound any more.
///
/// Falls back to the legacy points-Gaussian path when a projection has no stat
/// line, so pre-Epic-20 documents still simulate.
/// </summary>
public static class MonteCarloSimulationService
{
    public const int DefaultIterations = 10_000;

    // ── Stat-line variance (FAN-117) ──────────────────────────────────────
    //
    // Volume and efficiency coefficients of variation, per position. Calibrated
    // 2026-09-01 by simulating representative real stat lines from the dev data
    // and solving for the pair that reproduces the TOTAL variance of the legacy
    // model — so floors and ceilings stay on the same scale as before while the
    // distribution gains its proper shape.
    //
    //   RB  0.289/0.186 -> total CV 0.381  (legacy 0.38)
    //   WR  0.280/0.181 -> total CV 0.421  (legacy 0.42)
    //   TE  0.187/0.120 -> total CV 0.400  (legacy 0.40)
    //
    // QB is the exception and deliberately so. Passing and rushing touchdowns are
    // counts; their Poisson noise alone puts a QB's total CV at ~0.33, above the
    // 0.28 the legacy model assumed. No volume/efficiency setting can reach 0.28
    // once touchdowns are modelled honestly — the old figure was simply
    // under-dispersed. QB coefficients are therefore set near zero and QB variance
    // is essentially irreducible count noise. Expect QB floors and ceilings to
    // widen by roughly 15-20% relative to the old model. That is a correction,
    // not a regression.
    private static readonly Dictionary<string, (double Volume, double Efficiency)> StatLineVariance = new()
    {
        ["QB"] = (0.050, 0.030),
        ["RB"] = (0.289, 0.186),
        ["WR"] = (0.280, 0.181),
        ["TE"] = (0.187, 0.120),
    };

    private static readonly (double Volume, double Efficiency) DefaultStatLineVariance = (0.270, 0.175);

    /// <summary>
    /// Applied to both CVs when a projection came from the rookie prior rather than
    /// from game logs. Widens floor/ceiling by roughly 40% — a deliberate statement
    /// that a prior is not an observation.
    /// </summary>
    private const double RookieVarianceMultiplier = 1.40;

    // ── Legacy coefficients — used only by the fallback path ──────────────
    private static readonly Dictionary<string, decimal> PositionVariance = new()
    {
        ["QB"] = 0.28m,
        ["RB"] = 0.38m,
        ["WR"] = 0.42m,
        ["TE"] = 0.40m,
    };

    // Role modifiers scale VOLUME risk only — a handcuff's uncertainty is whether
    // he plays, not how well he runs when he does.
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
        return projection.StatLine is null || projection.StatLine.IsEmpty
            ? SimulateFromPoints(projection, role, iterations, seed)
            : SimulateFromStatLine(projection, projection.StatLine, role, iterations, seed);
    }

    // ── Stat-line simulation ──────────────────────────────────────────────

    private static SimulationResultDocument SimulateFromStatLine(
        PlayerProjectionDocument projection,
        ProjectedStatLine line,
        PlayerRole role,
        int iterations,
        int? seed)
    {
        var position = (projection.Position ?? string.Empty).ToUpperInvariant();

        // Scored full-PPR so BaseProjection and Median stay directly comparable
        // with every existing consumer. Any league format can be applied instead —
        // that is the point of scoring a stat line rather than storing points.
        var scoring = LeagueScoringSettings.FullPpr;

        var baseProjection = FantasyScoringService.Score(line, scoring, position);

        var (volCv, effCv) = StatLineVariance.TryGetValue(position, out var v)
            ? v
            : DefaultStatLineVariance;

        var roleModifier = (double)RoleVarianceModifier.GetValueOrDefault(role, 1.00m);
        volCv *= roleModifier;

        // A rookie prior is built from a depth chart and a stopwatch, not from
        // games. The central estimate is a reasonable guess; the uncertainty
        // around it is genuinely larger than for a player with a season of usage
        // behind him, and the distribution should say so rather than presenting a
        // prior with the same confidence as an observation.
        if (string.Equals(projection.Basis, nameof(ProjectionBasis.RookieProjection),
                          StringComparison.OrdinalIgnoreCase))
        {
            volCv *= RookieVarianceMultiplier;
            effCv *= RookieVarianceMultiplier;
        }

        // Per-opportunity rates, held fixed; volume and efficiency are what vary.
        var catchRate = Ratio(line.Receptions, line.Targets, 0.65);
        var yardsPerReception = Ratio(line.ReceivingYards, line.Receptions, 10.0);
        var yardsPerCarry = Ratio(line.RushingYards, line.Carries, 4.2);
        var yardsPerAttempt = Ratio(line.PassingYards, line.PassingAttempts, 7.0);

        var expTargets = (double)line.Targets;
        var expCarries = (double)line.Carries;
        var expAttempts = (double)line.PassingAttempts;

        var rng = seed.HasValue ? new Random(seed.Value) : new Random();
        var results = new double[iterations];

        for (var i = 0; i < iterations; i++)
        {
            var points = 0.0;

            // ── Passing ───────────────────────────────────────────────────
            if (expAttempts > 0)
            {
                var vol = Shock(rng, volCv);
                var eff = Shock(rng, effCv);

                points += expAttempts * vol * yardsPerAttempt * eff * (double)scoring.PointsPerPassingYard;
                points += SamplePoisson(rng, (double)line.PassingTds * vol) * (double)scoring.PassingTdPoints;
                points += SamplePoisson(rng, (double)line.Interceptions * vol) * (double)scoring.InterceptionPoints;
            }

            // ── Rushing ───────────────────────────────────────────────────
            if (expCarries > 0)
            {
                var vol = Shock(rng, volCv);
                var eff = Shock(rng, effCv);

                points += expCarries * vol * yardsPerCarry * eff * (double)scoring.PointsPerRushingYard;
                points += SamplePoisson(rng, (double)line.RushingTds * vol) * (double)scoring.RushingTdPoints;
            }

            // ── Receiving ─────────────────────────────────────────────────
            if (expTargets > 0)
            {
                var vol = Shock(rng, volCv);
                var eff = Shock(rng, effCv);

                // Targets are a count, and receptions are a binomial draw from
                // them — which is what gives PPR formats their genuine floor.
                var targets = (int)Math.Round(expTargets * vol, MidpointRounding.AwayFromZero);
                var receptions = SampleBinomial(rng, targets, catchRate);

                points += receptions * (double)scoring.PointsPerReception;
                points += receptions * yardsPerReception * eff * (double)scoring.PointsPerReceivingYard;
                points += SamplePoisson(rng, (double)line.ReceivingTds * vol) * (double)scoring.ReceivingTdPoints;

                if (string.Equals(position, "TE", StringComparison.OrdinalIgnoreCase))
                    points += receptions * (double)scoring.BonusRecTe;
            }

            // ── Misc ──────────────────────────────────────────────────────
            points += SamplePoisson(rng, (double)line.FumblesLost) * (double)scoring.FumbleLostPoints;
            points += SamplePoisson(rng, (double)line.TwoPointConversions) * (double)scoring.TwoPointConversionPoints;
            points += SamplePoisson(rng, (double)line.SpecialTeamsTds) * (double)scoring.SpecialTeamsTdPoints;

            results[i] = Math.Max(0.0, points);
        }

        return BuildResult(projection, role, iterations, results, baseProjection,
            standardDeviation: (decimal)SampleStdDev(results));
    }

    // ── Legacy fallback — projections written before Epic 20 ──────────────

    private static SimulationResultDocument SimulateFromPoints(
        PlayerProjectionDocument projection,
        PlayerRole role,
        int iterations,
        int? seed)
    {
        var baseProjection = projection.ProjectedPointsPpr;

        var positionCoeff = PositionVariance.GetValueOrDefault(projection.Position, 0.40m);
        var roleModifier = RoleVarianceModifier.GetValueOrDefault(role, 1.00m);
        var stdDev = baseProjection * positionCoeff * roleModifier;

        var rng = seed.HasValue ? new Random(seed.Value) : new Random();
        var results = new double[iterations];

        for (var i = 0; i < iterations; i++)
        {
            var z = NextGaussian(rng);
            results[i] = Math.Max(0.0, (double)baseProjection + z * (double)stdDev);
        }

        return BuildResult(projection, role, iterations, results, baseProjection, stdDev);
    }

    // ── Shared assembly ───────────────────────────────────────────────────

    private static SimulationResultDocument BuildResult(
        PlayerProjectionDocument projection,
        PlayerRole role,
        int iterations,
        double[] results,
        decimal baseProjection,
        decimal standardDeviation)
    {
        Array.Sort(results);

        var floor = (decimal)results[(int)(iterations * 0.10)];
        var median = (decimal)results[(int)(iterations * 0.50)];
        var ceiling = (decimal)results[(int)(iterations * 0.90)];
        var mean = (decimal)results.Average();

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
            StandardDeviation = Math.Round(standardDeviation, 2),
            Floor = Math.Round(floor, 2),
            Median = Math.Round(median, 2),
            Ceiling = Math.Round(ceiling, 2),
            Mean = Math.Round(mean, 2),
            BoomProbability = Math.Round(boomProbability, 4),
            BustProbability = Math.Round(bustProbability, 4),
            PlayerRole = role.ToString(),
            ScoringFormat = "FullPpr",
            CalculatedAt = DateTime.UtcNow
        };
    }

    // ── Sampling primitives ───────────────────────────────────────────────
    // Implemented locally rather than pulled from MathNet so the simulation has no
    // distribution-library surface to drift against. All lambdas here are small
    // (< 3) and binomial n is roughly 3-15, so the naive algorithms are the fast
    // ones at this scale.

    /// <summary>Multiplicative shock, mean 1, floored at zero.</summary>
    private static double Shock(Random rng, double cv)
        => cv <= 0 ? 1.0 : Math.Max(0.0, 1.0 + NextGaussian(rng) * cv);

    /// <summary>Standard normal via Box-Muller.</summary>
    private static double NextGaussian(Random rng)
    {
        var u1 = 1.0 - rng.NextDouble();
        var u2 = 1.0 - rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>Knuth's product-of-uniforms Poisson sampler. O(lambda).</summary>
    private static int SamplePoisson(Random rng, double lambda)
    {
        if (lambda <= 0) return 0;
        if (lambda > 30) return (int)Math.Round(lambda + NextGaussian(rng) * Math.Sqrt(lambda));

        var l = Math.Exp(-lambda);
        var k = 0;
        var p = 1.0;

        do
        {
            k++;
            p *= rng.NextDouble();
        }
        while (p > l);

        return k - 1;
    }

    /// <summary>Sum of Bernoulli trials. n is small here, so this is cheap.</summary>
    private static int SampleBinomial(Random rng, int n, double p)
    {
        if (n <= 0 || p <= 0) return 0;
        if (p >= 1) return n;

        var count = 0;
        for (var i = 0; i < n; i++)
            if (rng.NextDouble() < p) count++;

        return count;
    }

    private static double Ratio(decimal numerator, decimal denominator, double fallback)
        => denominator <= 0m ? fallback : (double)numerator / (double)denominator;

    private static double SampleStdDev(double[] values)
    {
        if (values.Length < 2) return 0.0;
        var mean = values.Average();
        var sumSq = 0.0;
        foreach (var v in values) sumSq += (v - mean) * (v - mean);
        return Math.Sqrt(sumSq / (values.Length - 1));
    }
}

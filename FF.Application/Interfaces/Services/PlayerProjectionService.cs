// FF.Application/Services/PlayerProjectionService.cs
using FF.Domain.Documents;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace FF.Application.Interfaces.Services;

public class PlayerProjectionService
{
    // Produces projections in all three scoring formats
    public static PlayerProjectionResult Project(ProjectionInput input)
    {
        var logs = input.GameLogs
            .Where(g => DidPlay(g, input.Position))
            .OrderByDescending(g => g.Season * 100 + g.Week)
            .ToList();

        if (logs.Count < input.Weights.MinGamesRequired)
            return PlayerProjectionResult.Insufficient(input.PlayerId);

        // Build weight vector — more recent games get higher weight
        var gameWeights = BuildRecencyWeights(logs.Count, input.Weights.RecentGameWeight);

        // Project each scoring format independently
        var std = RunRegression(
            [.. logs.Select(g => (double)(g.FantasyPoints ?? 0m))],
            gameWeights);

        var ppr = RunRegression(
            [.. logs.Select(g => (double)(g.FantasyPointsPpr ?? 0m))],
            gameWeights);

        var halfPpr = RunRegression(
            [.. logs.Select(g => (double)(((g.FantasyPoints ?? 0m) + (g.FantasyPointsPpr ?? 0m)) / 2m))],
            gameWeights);

        // Matchup adjustment: DifficultyScore 0-100, 50=neutral. Scale to ±20%
        var matchupFactor = 1m + ((50m - input.MatchupDifficultyScore) / 50m) * 0.20m;

        return new PlayerProjectionResult
        {
            PlayerId = input.PlayerId,
            ProjectedPoints = Math.Max(0m, (decimal)std.Projection * matchupFactor),
            ProjectedPointsPpr = Math.Max(0m, (decimal)ppr.Projection * matchupFactor),
            ProjectedPointsHalfPpr = Math.Max(0m, (decimal)halfPpr.Projection * matchupFactor),
            WeightedAvgPoints = (decimal)std.WeightedAverage,
            MatchupAdjustmentFactor = matchupFactor,
            SnapPctInput = input.SnapPct,
            TargetShareInput = input.TargetShare,
            GameSampleSize = logs.Count,
            RSquared = (decimal)std.RSquared
        };
    }
    private static bool DidPlay(PlayerGameLogDocument g, string position) =>
    position switch
    {
        "QB" => g.Completions > 0 || g.PassingYards > 0,
        "RB" => g.Carries > 0 || g.Targets > 0 || g.OffenseSnaps > 0,
        "WR" => g.Targets > 0 || g.ReceivingYards > 0 || g.OffenseSnaps > 0,
        "TE" => g.Targets > 0 || g.ReceivingYards > 0 || g.OffenseSnaps > 0,
        _ => g.Targets > 0 || g.Carries > 0 || g.OffenseSnaps > 0
    };

    private static double[] BuildRecencyWeights(int count, decimal recentBias)
    {
        // Exponential decay: most recent game gets weight 1.0, oldest gets (1-recentBias)
        var weights = new double[count];
        for (int i = 0; i < count; i++)
        {
            // i=0 is most recent
            weights[i] = Math.Pow((double)(1m - recentBias), i / (double)(count - 1));
        }
        return weights;
    }

    private static RegressionResult RunRegression(double[] yValues, double[] weights)
    {
        int n = yValues.Length;
        // Simple weighted mean as baseline — extend to WLS with features in PBI-023a
        double weightSum = weights.Sum();
        double weightedMean = yValues.Zip(weights, (y, w) => y * w).Sum() / weightSum;

        // Weighted least squares: regress fantasy points on week index (trend line)
        // X = [1, t] where t is normalized week index 0..1
        var X = DenseMatrix.OfArray(new double[n, 2]);
        var y = DenseVector.OfArray(yValues);
        var W = DenseMatrix.OfDiagonalArray(weights);

        for (int i = 0; i < n; i++)
        {
            X[i, 0] = 1.0;
            X[i, 1] = (double)i / (n - 1); // 0 = most recent, 1 = oldest
        }

        // β = (X'WX)⁻¹ X'Wy
        var Xt = X.Transpose();
        var XtW = Xt * W;
        var XtWX = XtW * X;
        var XtWy = XtW * y;
        var beta = XtWX.Solve(XtWy);

        // Projection = intercept (β[0]) — at t=0 (most recent)
        double projection = beta[0];

        // R² weighted
        double ss_res = 0, ss_tot = 0;
        for (int i = 0; i < n; i++)
        {
            double fitted = beta[0] + beta[1] * X[i, 1];
            ss_res += weights[i] * Math.Pow(yValues[i] - fitted, 2);
            ss_tot += weights[i] * Math.Pow(yValues[i] - weightedMean, 2);
        }
        double rSquared = ss_tot < 1e-10 ? 0 : Math.Max(0, 1 - ss_res / ss_tot);

        return new RegressionResult(projection, weightedMean, rSquared);
    }

    private record RegressionResult(double Projection, double WeightedAverage, double RSquared);
}

public class PlayerProjectionResult
{
    public string PlayerId { get; set; } = string.Empty;
    public decimal ProjectedPoints { get; set; }
    public decimal ProjectedPointsPpr { get; set; }
    public decimal ProjectedPointsHalfPpr { get; set; }
    public decimal WeightedAvgPoints { get; set; }
    public decimal MatchupAdjustmentFactor { get; set; }
    public decimal SnapPctInput { get; set; }
    public decimal TargetShareInput { get; set; }
    public int GameSampleSize { get; set; }
    public decimal RSquared { get; set; }
    public bool IsInsufficient { get; set; }

    public static PlayerProjectionResult Insufficient(string playerId) =>
        new() { PlayerId = playerId, IsInsufficient = true };
}
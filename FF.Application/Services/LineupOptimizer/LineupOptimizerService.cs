// FF.Application/Services/LineupOptimizer/LineupOptimizerService.cs
using FF.Domain.Enums;
using Google.OrTools.Sat;

namespace FF.Application.Services.LineupOptimizer;

public static class LineupOptimizerService
{
    private const int ScaleFactor = 100;

    public static LineupOptimizerResult Optimize(LineupOptimizerInput input)
    {
        var players = input.AvailablePlayers
            .Where(p => !p.IsExcluded)
            .ToList();

        if (players.Count == 0)
            return LineupOptimizerResult.Failed("No eligible players available.");

        var config = input.RosterConfig;

        if (players.Count(p => p.Position == "QB") < config.QbSlots)
            return LineupOptimizerResult.Failed("Insufficient QB players available.");
        if (players.Count(p => p.Position == "RB") < config.RbSlots)
            return LineupOptimizerResult.Failed("Insufficient RB players available.");
        if (players.Count(p => p.Position == "WR") < config.WrSlots)
            return LineupOptimizerResult.Failed("Insufficient WR players available.");
        if (players.Count(p => p.Position == "TE") < config.TeSlots)
            return LineupOptimizerResult.Failed("Insufficient TE players available.");

        var model = new CpModel();
        var solver = new CpSolver();

        var x = players.Select(_ => model.NewBoolVar("x")).ToArray();

        var scaledScores = players
            .Select(p => (long)(GetEffectiveScore(p, input.Mode, input.RiskProfile) * ScaleFactor))
            .ToArray();
        model.Maximize(LinearExpr.WeightedSum(x, scaledScores));

        var qbIdx = players.Select((p, i) => new { p, i }).Where(z => z.p.Position == "QB").Select(z => z.i).ToList();
        var rbIdx = players.Select((p, i) => new { p, i }).Where(z => z.p.Position == "RB").Select(z => z.i).ToList();
        var wrIdx = players.Select((p, i) => new { p, i }).Where(z => z.p.Position == "WR").Select(z => z.i).ToList();
        var teIdx = players.Select((p, i) => new { p, i }).Where(z => z.p.Position == "TE").Select(z => z.i).ToList();

        model.Add(LinearExpr.Sum([.. qbIdx.Select(i => x[i])]) == config.QbSlots);
        model.Add(LinearExpr.Sum([.. rbIdx.Select(i => x[i])]) >= config.RbSlots);
        model.Add(LinearExpr.Sum([.. wrIdx.Select(i => x[i])]) >= config.WrSlots);
        model.Add(LinearExpr.Sum([.. teIdx.Select(i => x[i])]) >= config.TeSlots);
        model.Add(LinearExpr.Sum(x) == config.TotalStarters);

        foreach (var i in Enumerable.Range(0, players.Count))
            if (input.LockedPlayerIds.Contains(players[i].PlayerId))
                model.Add(x[i] == 1);

        solver.StringParameters = "max_time_in_seconds:10.0";
        var status = solver.Solve(model);

        if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
            return LineupOptimizerResult.Failed($"Solver returned status: {status}");

        var selected = Enumerable.Range(0, players.Count)
            .Where(i => solver.BooleanValue(x[i]))
            .Select(i => players[i])
            .ToList();

        var lineup = new List<OptimizedSlot>();
        var rbFilled = 0;
        var wrFilled = 0;
        var teFilled = 0;

        foreach (var p in selected.OrderByDescending(p => GetEffectiveScore(p, input.Mode, input.RiskProfile)))
        {
            string slotType;

            if (p.Position == "QB")
            {
                slotType = "QB";
            }
            else if (p.Position == "RB" && rbFilled < config.RbSlots)
            {
                slotType = "RB";
                rbFilled++;
            }
            else if (p.Position == "WR" && wrFilled < config.WrSlots)
            {
                slotType = "WR";
                wrFilled++;
            }
            else if (p.Position == "TE" && teFilled < config.TeSlots)
            {
                slotType = "TE";
                teFilled++;
            }
            else
            {
                slotType = "FLEX";
            }

            lineup.Add(new OptimizedSlot
            {
                PlayerId = p.PlayerId,
                PlayerName = p.PlayerName,
                Position = p.Position,
                SlotType = slotType,
                ProjectedPoints = GetEffectiveScore(p, input.Mode, input.RiskProfile),
                RiskScore = input.RiskProfile.HasValue
                    ? GetRiskScore(p, input.RiskProfile.Value)
                    : null
            });
        }

        return new LineupOptimizerResult
        {
            Success = true,
            Lineup = lineup,
            TotalProjectedPoints = Math.Round(lineup.Sum(s => s.ProjectedPoints), 2),
            Mode = input.Mode,
            RiskProfile = input.RiskProfile
        };
    }

    /// <summary>
    /// Dispatches to risk scoring when a RiskProfile is set, otherwise falls back
    /// to the legacy OptimizationMode score. RiskProfile always wins when present.
    /// </summary>
    private static decimal GetEffectiveScore(
        PlayerSlot player,
        OptimizationMode mode,
        RiskProfile? riskProfile) =>
        riskProfile.HasValue
            ? GetRiskScore(player, riskProfile.Value)
            : GetModeScore(player, mode);

    /// <summary>Legacy OptimizationMode scoring — unchanged from PBI-027.</summary>
    private static decimal GetModeScore(PlayerSlot player, OptimizationMode mode) =>
        mode switch
        {
            OptimizationMode.Floor => player.ProjectedFloor,
            OptimizationMode.Ceiling => player.ProjectedCeiling,
            _ => player.ProjectedMedian
        };

    /// <summary>
    /// Risk-adjusted scoring.
    ///
    /// Safe:        Floor  − (BustProbability × 10)
    ///              Rewards consistent, high-floor players. Penalises bust risk.
    ///
    /// Ceiling:     Ceiling + (BoomProbability × 10)
    ///              Rewards explosive upside. Accepts bust risk.
    ///
    /// Contrarian:  Ceiling + (BoomProbability × 8) − (OwnershipPct × 0.5)
    ///              Seeks high-upside, low-ownership differentials.
    ///
    /// BoomProbability and BustProbability are 0-1 fractions from Monte Carlo output.
    /// Multipliers (×10, ×8) are tunable — they express ~1 fantasy point per 10% probability.
    /// </summary>
    private static decimal GetRiskScore(PlayerSlot player, RiskProfile profile) =>
        profile switch
        {
            RiskProfile.Safe =>
                player.ProjectedFloor
                - (player.BustProbability ?? 0m) * 10m,

            RiskProfile.Ceiling =>
                player.ProjectedCeiling
                + (player.BoomProbability ?? 0m) * 10m,

            RiskProfile.Contrarian =>
                player.ProjectedCeiling
                + (player.BoomProbability ?? 0m) * 8m
                - (player.OwnershipPct ?? 0m) * 0.5m,

            _ => player.ProjectedMedian
        };
}
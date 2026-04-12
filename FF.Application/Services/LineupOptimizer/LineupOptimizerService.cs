// FF.Application/Services/LineupOptimizer/LineupOptimizerService.cs
using FF.Domain.Enums;
using FF.Domain.ValueObjects;
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
        var n = players.Count;

        // ── Preflight checks ─────────────────────────────────────────────
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

        // ── Decision variables ───────────────────────────────────────────
        // x[i]         = player i is selected in the lineup (any slot)
        // qbVar[i]     = player i fills a QB slot
        // rbVar[i]     = player i fills an RB slot
        // wrVar[i]     = player i fills a WR slot
        // teVar[i]     = player i fills a TE slot
        // flexVar[s][i] = player i fills flex slot s

        var x = Enumerable.Range(0, n).Select(_ => model.NewBoolVar("x")).ToArray();
        var qbVar = Enumerable.Range(0, n).Select(_ => model.NewBoolVar("qb")).ToArray();
        var rbVar = Enumerable.Range(0, n).Select(_ => model.NewBoolVar("rb")).ToArray();
        var wrVar = Enumerable.Range(0, n).Select(_ => model.NewBoolVar("wr")).ToArray();
        var teVar = Enumerable.Range(0, n).Select(_ => model.NewBoolVar("te")).ToArray();

        var flexCount = config.FlexSlotDefinitions.Count;
        var flexVar = Enumerable.Range(0, flexCount)
            .Select(_ => Enumerable.Range(0, n)
                .Select(_ => model.NewBoolVar("fl"))
                .ToArray())
            .ToArray();

        // ── Objective — maximize projected score ─────────────────────────
        var scaledScores = players
            .Select(p => (long)(GetEffectiveScore(p, input.Mode, input.RiskProfile) * ScaleFactor))
            .ToArray();
        model.Maximize(LinearExpr.WeightedSum(x, scaledScores));

        // ── Slot assignment constraints ──────────────────────────────────
        // Each player can only be assigned to one slot type
        for (var i = 0; i < n; i++)
        {
            var slotVars = new List<ILiteral> { qbVar[i], rbVar[i], wrVar[i], teVar[i] };
            for (var s = 0; s < flexCount; s++)
                slotVars.Add(flexVar[s][i]);

            // Sum of all slot assignments == x[i] (selected or not)
            model.Add(LinearExpr.Sum(slotVars.Cast<BoolVar>()) == x[i]);
        }

        // ── Position eligibility — dedicated slots ───────────────────────
        // QB slot: only QBs
        for (var i = 0; i < n; i++)
            if (players[i].Position != "QB")
                model.Add(qbVar[i] == 0);

        // RB slot: only RBs
        for (var i = 0; i < n; i++)
            if (players[i].Position != "RB")
                model.Add(rbVar[i] == 0);

        // WR slot: only WRs
        for (var i = 0; i < n; i++)
            if (players[i].Position != "WR")
                model.Add(wrVar[i] == 0);

        // TE slot: only TEs
        for (var i = 0; i < n; i++)
            if (players[i].Position != "TE")
                model.Add(teVar[i] == 0);

        // ── Position eligibility — flex slots ────────────────────────────
        // Each flex slot enforces its own EligiblePositions list
        for (var s = 0; s < flexCount; s++)
        {
            var slotDef = config.FlexSlotDefinitions[s];
            for (var i = 0; i < n; i++)
                if (!slotDef.IsEligible(players[i].Position))
                    model.Add(flexVar[s][i] == 0);
        }

        // ── Slot count constraints ────────────────────────────────────────
        model.Add(LinearExpr.Sum(qbVar) == config.QbSlots);
        model.Add(LinearExpr.Sum(rbVar) == config.RbSlots);
        model.Add(LinearExpr.Sum(wrVar) == config.WrSlots);
        model.Add(LinearExpr.Sum(teVar) == config.TeSlots);

        // Each flex slot fills exactly 1 player
        for (var s = 0; s < flexCount; s++)
            model.Add(LinearExpr.Sum(flexVar[s]) == 1);

        // Total starters
        model.Add(LinearExpr.Sum(x) == config.TotalStarters);

        // ── Lock constraints ─────────────────────────────────────────────
        for (var i = 0; i < n; i++)
            if (input.LockedPlayerIds.Contains(players[i].PlayerId))
                model.Add(x[i] == 1);

        // ── Solve ────────────────────────────────────────────────────────
        solver.StringParameters = "max_time_in_seconds:10.0";
        var status = solver.Solve(model);

        if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
            return LineupOptimizerResult.Failed($"Solver returned status: {status}");

        // ── Build result — slot type from variable values ─────────────────
        var lineup = new List<OptimizedSlot>();

        for (var i = 0; i < n; i++)
        {
            if (!solver.BooleanValue(x[i])) continue;

            string slotType;

            if (solver.BooleanValue(qbVar[i]))
                slotType = "QB";
            else if (solver.BooleanValue(rbVar[i]))
                slotType = "RB";
            else if (solver.BooleanValue(wrVar[i]))
                slotType = "WR";
            else if (solver.BooleanValue(teVar[i]))
                slotType = "TE";
            else
            {
                // Determine which flex slot this player filled
                // and label it appropriately
                var filledSlot = -1;
                for (var s = 0; s < flexCount; s++)
                {
                    if (!solver.BooleanValue(flexVar[s][i])) continue;
                    filledSlot = s;
                    break;
                }

                // SUPERFLEX slot = flex slot whose eligible positions include QB
                slotType = filledSlot >= 0 &&
                           config.FlexSlotDefinitions[filledSlot].IsEligible("QB")
                    ? "SUPERFLEX"
                    : "FLEX";
            }

            lineup.Add(new OptimizedSlot
            {
                PlayerId = players[i].PlayerId,
                PlayerName = players[i].PlayerName,
                Position = players[i].Position,
                SlotType = slotType,
                ProjectedPoints = GetEffectiveScore(players[i], input.Mode, input.RiskProfile),
                RiskScore = input.RiskProfile.HasValue
                    ? GetRiskScore(players[i], input.RiskProfile.Value)
                    : null
            });
        }

        // Sort for display: QB → RB → WR → TE → FLEX → SUPERFLEX
        lineup = lineup
            .OrderBy(s => SlotOrder(s.SlotType))
            .ThenByDescending(s => s.ProjectedPoints)
            .ToList();

        return new LineupOptimizerResult
        {
            Success = true,
            Lineup = lineup,
            TotalProjectedPoints = Math.Round(lineup.Sum(s => s.ProjectedPoints), 2),
            Mode = input.Mode,
            RiskProfile = input.RiskProfile
        };
    }

    private static int SlotOrder(string slotType) => slotType switch
    {
        "QB" => 0,
        "RB" => 1,
        "WR" => 2,
        "TE" => 3,
        "FLEX" => 4,
        "SUPERFLEX" => 5,
        _ => 6
    };

    private static decimal GetEffectiveScore(
        PlayerSlot player,
        OptimizationMode mode,
        RiskProfile? riskProfile) =>
        riskProfile.HasValue
            ? GetRiskScore(player, riskProfile.Value)
            : GetModeScore(player, mode);

    private static decimal GetModeScore(PlayerSlot player, OptimizationMode mode) =>
        mode switch
        {
            OptimizationMode.Floor => player.ProjectedFloor,
            OptimizationMode.Ceiling => player.ProjectedCeiling,
            _ => player.ProjectedMedian
        };

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
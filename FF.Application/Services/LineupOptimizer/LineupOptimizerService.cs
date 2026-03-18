// FF.Application/Services/LineupOptimizer/LineupOptimizerService.cs
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

        // One binary variable per player — selected or not
        var x = players.Select(_ => model.NewBoolVar("x")).ToArray();

        // Objective — maximise scaled integer scores
        var scaledScores = players
            .Select(p => (long)(GetScore(p, input.Mode) * ScaleFactor))
            .ToArray();
        model.Maximize(LinearExpr.WeightedSum(x, scaledScores));

        // Positional index groups
        var qbIdx = players.Select((p, i) => new { p, i }).Where(z => z.p.Position == "QB").Select(z => z.i).ToList();
        var rbIdx = players.Select((p, i) => new { p, i }).Where(z => z.p.Position == "RB").Select(z => z.i).ToList();
        var wrIdx = players.Select((p, i) => new { p, i }).Where(z => z.p.Position == "WR").Select(z => z.i).ToList();
        var teIdx = players.Select((p, i) => new { p, i }).Where(z => z.p.Position == "TE").Select(z => z.i).ToList();
        var flexIdx = rbIdx.Concat(wrIdx).Concat(teIdx).ToList();

        // Exact QB count
        model.Add(LinearExpr.Sum(qbIdx.Select(i => x[i]).ToArray()) == config.QbSlots);

        // Positional minimums
        model.Add(LinearExpr.Sum(rbIdx.Select(i => x[i]).ToArray()) >= config.RbSlots);
        model.Add(LinearExpr.Sum(wrIdx.Select(i => x[i]).ToArray()) >= config.WrSlots);
        model.Add(LinearExpr.Sum(teIdx.Select(i => x[i]).ToArray()) >= config.TeSlots);

        // Total starters
        model.Add(LinearExpr.Sum(x) == config.TotalStarters);

        // FLEX count = total selected - QB slots - RB slots - WR slots - TE slots
        // This is implicitly enforced by TotalStarters constraint above

        // Locked players
        foreach (var i in Enumerable.Range(0, players.Count))
            if (input.LockedPlayerIds.Contains(players[i].PlayerId))
                model.Add(x[i] == 1);

        // Solve
        solver.StringParameters = "max_time_in_seconds:10.0";
        var status = solver.Solve(model);

        if (status is not (CpSolverStatus.Optimal or CpSolverStatus.Feasible))
            return LineupOptimizerResult.Failed($"Solver returned status: {status}");

        // Build result — assign slots post-solve by filling minimums first
        var selected = Enumerable.Range(0, players.Count)
            .Where(i => solver.BooleanValue(x[i]))
            .Select(i => players[i])
            .ToList();

        var lineup = new List<OptimizedSlot>();
        var rbFilled = 0;
        var wrFilled = 0;
        var teFilled = 0;

        // First pass: fill positional slots up to minimums, sorted by score desc
        var remaining = new List<(string position, string id, string name, string team, decimal pts)>();

        foreach (var p in selected.OrderByDescending(p => GetScore(p, input.Mode)))
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
                ProjectedPoints = GetScore(p, input.Mode)
            });
        }

        return new LineupOptimizerResult
        {
            Success = true,
            Lineup = lineup,
            TotalProjectedPoints = Math.Round(lineup.Sum(s => s.ProjectedPoints), 2),
            Mode = input.Mode
        };
    }
    private static decimal GetScore(PlayerSlot player, OptimizationMode mode) =>
        mode switch
        {
            OptimizationMode.Floor => player.ProjectedFloor,
            OptimizationMode.Ceiling => player.ProjectedCeiling,
            _ => player.ProjectedMedian
        };
}
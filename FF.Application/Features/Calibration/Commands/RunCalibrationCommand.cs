// FF.Application/Features/Calibration/Commands/RunCalibrationCommand.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.Calibration.Commands;

public record RunCalibrationCommand(int Season, string ScoringFormat = "Superflex") : IRequest<RunCalibrationResult>;

public record RunCalibrationResult(
    double SpearmanRho,
    double AvgAbsDelta,
    int Top10Overlap,
    int PlayerCount,
    List<CalibrationPlayerSnapshot> Top20Snapshot,
    int UnmatchedCount = 0,
    List<string>? TopUnmatched = null,
    List<CalibrationPlayerSnapshot>? Worst20Snapshot = null);

public class RunCalibrationCommandHandler(
    IDynastyValuationRepository valuationRepo,
    IFantasyProsRookieRankingRepository fpRookieRepo,
    ICalibrationResultRepository calibrationRepo) : IRequestHandler<RunCalibrationCommand, RunCalibrationResult>
{
    public async Task<RunCalibrationResult> Handle(RunCalibrationCommand request, CancellationToken ct)
    {
        // Load our dynasty valuations (all, sorted by TradeValue desc)
        var ourValuations = await valuationRepo.GetTopByTradeValueAsync(250, position: null, ct);

        // Load FantasyPros dynasty rankings — we use the imported FP rookie+veteran rankings
        // FP overall dynasty rankings are stored in fantasyPros_rookie_rankings for the current season
        var fpRankings = await fpRookieRepo.GetAllBySeasonAndTypeAsync(request.Season, "Dynasty", ct);

        if (fpRankings.Count == 0)
            throw new InvalidOperationException(
                $"No FantasyPros dynasty rankings found for season {request.Season}. " +
                "Import the FP Dynasty CSV on the Admin Imports page first.");

        // Build a lookup: SleeperPlayerId → FP rank
        var fpBySleeperIdRank = fpRankings
            .Where(f => !string.IsNullOrEmpty(f.SleeperPlayerId))
            .ToDictionary(f => f.SleeperPlayerId, f => f.FantasyProsRank);

        // Match our valuations to FP — only players present in both lists.
        //
        // 2026-09-07: this join is lossy and used to be silent about it. Anyone we
        // value who has no FantasyPros row — or whose FP row carries no Sleeper id
        // — vanishes from every metric below with no trace in the output. That is
        // not a rounding error: Patrick Mahomes, our #1, is one of the players it
        // drops, which is exactly why the calibration table and the Dynasty
        // Rankings page show different leaders. Count what falls out and name the
        // most valuable casualties, so the numbers can be read for what they are.
        var matched = ourValuations
            .Where(v => fpBySleeperIdRank.ContainsKey(v.SleeperPlayerId))
            .OrderByDescending(v => v.TradeValue)
            .Select((v, idx) => new
            {
                OurRank = idx + 1,
                v.PlayerName,
                v.Position,
                v.TradeValue,
                FpRank = fpBySleeperIdRank[v.SleeperPlayerId]
            })
            .ToList();

        var unmatched = ourValuations
            .Where(v => !fpBySleeperIdRank.ContainsKey(v.SleeperPlayerId))
            .OrderByDescending(v => v.TradeValue)
            .ToList();

        var topUnmatched = unmatched
            .Take(10)
            .Select(v => $"{v.PlayerName} ({v.Position}, TV {Math.Round(v.TradeValue, 1)})")
            .ToList();

        int n = Math.Min(matched.Count, 200);
        if (n < 10)
            throw new InvalidOperationException(
                $"Only {n} players matched between our valuations and FP rankings. " +
                "Run DFV calculation and re-import FP rankings before calibrating.");

        var subset = matched.Take(n).ToList();

        // Spearman's simplified d² shortcut formula (below) is only valid when BOTH rankings
        // are dense permutations of 1..n over the same n items. OurRank already is one — it's
        // assigned by position within this matched subset. Raw FpRank is NOT: it's FantasyPros'
        // rank within their own much larger full-population list, so within this subset it has
        // gaps (e.g. FpRank 52 when the subset only has ~100 players in it). Feeding a dense
        // rank and a sparse rank into the shortcut formula breaks its bounds and produces rho
        // far outside the mathematically valid [-1, 1] range — this is why every prior
        // calibration run (-14.5, -6.1, -4.7, -6.88, ...) showed an impossible rho. It was never
        // a model-quality signal; the harness itself was miscomputing the statistic.
        //
        // Fix: dense-rank FpRank within this same matched subset (ties get the average rank,
        // standard Spearman tie handling) before computing d_i, so both series are proper 1..n
        // rankings over the identical population and the shortcut formula's assumptions hold.
        var fpDenseRankBySubsetPosition = subset
            .Select(p => p.FpRank)
            .OrderBy(r => r)
            .Select((r, i) => new { r, position = i + 1 })
            .GroupBy(x => x.r)
            .ToDictionary(g => g.Key, g => g.Average(x => (double)x.position));

        // Spearman ρ — our dense rank vs. FP's dense rank within this matched subset
        double sumD2 = subset.Sum(p => Math.Pow(p.OurRank - fpDenseRankBySubsetPosition[p.FpRank], 2));
        double rho = 1.0 - (6.0 * sumD2) / ((double)n * (n * n - 1));

        // Avg absolute delta — same dense-rank basis as ρ, so it measures the same comparison
        double avgDelta = subset.Average(p => Math.Abs(p.OurRank - fpDenseRankBySubsetPosition[p.FpRank]));

        // Top-10 overlap — on the matched subset, both sides.
        //
        // 2026-09-07: this used to take OUR top 10 from the unmatched list and
        // compare it to FP's top 10 by raw rank, so it ran on a third population,
        // different from the one ρ and Avg |Δ| use. Worse, it was capped: a player
        // we rank in the top 10 who has no FP row at all occupies one of our ten
        // slots and can never overlap with anything, so the metric could not reach
        // 10/10 no matter how well calibrated the model was. With Mahomes
        // unmatched, the ceiling was 9.
        var ourTop10 = subset.Take(10)
            .Select(p => p.PlayerName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var fpTop10 = subset
            .OrderBy(p => fpDenseRankBySubsetPosition[p.FpRank])
            .Take(10)
            .Select(p => p.PlayerName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int top10Overlap = ourTop10.Intersect(fpTop10).Count();

        // Snapshot builder, shared by both views below so they cannot drift apart.
        // Takes primitives rather than the anonymous type: `dynamic` would bind at
        // runtime, and a typo here would surface as an exception during a
        // calibration run rather than a compile error.
        CalibrationPlayerSnapshot MakeSnapshot(
            int ourRank, string playerName, string position, double tradeValue, int fpRank)
        {
            var fpSubsetRank = fpDenseRankBySubsetPosition[fpRank];
            return new CalibrationPlayerSnapshot
            {
                OurRank = ourRank,
                PlayerName = playerName,
                Position = position,
                OurTradeValue = Math.Round(tradeValue, 1),
                FpRank = fpRank,
                FpSubsetRank = Math.Round(fpSubsetRank, 1),
                Delta = Math.Round(ourRank - fpSubsetRank, 1)
            };
        }

        // Top-20 snapshot
        // Delta is on the SUBSET rank, matching Avg |Δ| above. It used to use the
        // raw FpRank while the headline used the subset rank, so the two disagreed
        // and the column could not be averaged to reach the number above it.
        var snapshot = subset.Take(20)
            .Select(p => MakeSnapshot(p.OurRank, p.PlayerName, p.Position, p.TradeValue, p.FpRank))
            .ToList();

        // The twenty biggest disagreements anywhere in the population. This is the
        // view that actually points at the error: the top-20 table above sits
        // comfortably inside the Avg |Δ| target while the tail is three times
        // worse, so the headline metric can only be moved from down here.
        var worstSnapshot = subset
            .OrderByDescending(p => Math.Abs(p.OurRank - fpDenseRankBySubsetPosition[p.FpRank]))
            .ThenBy(p => p.OurRank)
            .Take(20)
            .Select(p => MakeSnapshot(p.OurRank, p.PlayerName, p.Position, p.TradeValue, p.FpRank))
            .ToList();

        // Persist result
        var doc = new CalibrationResultDocument
        {
            Id = Guid.NewGuid().ToString(),
            RunAt = DateTime.UtcNow,
            ScoringFormat = request.ScoringFormat,
            SpearmanRho = Math.Round(rho, 4),
            AvgAbsDelta = Math.Round(avgDelta, 2),
            Top10Overlap = top10Overlap,
            PlayerCount = n,
            Top20Snapshot = snapshot,
            Worst20Snapshot = worstSnapshot,
            UnmatchedCount = unmatched.Count,
            TopUnmatched = topUnmatched
        };

        await calibrationRepo.InsertAsync(doc, ct);

        return new RunCalibrationResult(
            doc.SpearmanRho, doc.AvgAbsDelta, top10Overlap, n, snapshot,
            unmatched.Count, topUnmatched, worstSnapshot);
    }
}
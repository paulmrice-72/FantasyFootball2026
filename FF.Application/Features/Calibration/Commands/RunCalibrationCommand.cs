// FF.Application/Features/Calibration/Commands/RunCalibrationCommand.cs
using FF.Application.Interfaces.Persistence;
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using MediatR;

namespace FF.Application.Features.Calibration.Commands;

/// <summary>
/// FAN-159. <paramref name="ValueBasis"/> selects which of our two values the run
/// ranks players by:
///
/// <list type="bullet">
/// <item><c>Model</c> (default) — <c>ModelValue</c>, the pipeline with every
/// FantasyPros-derived step removed. This is the only basis on which ρ means
/// what its name says.</item>
/// <item><c>Blended</c> — <c>TradeValue</c>, what the site serves. Kept so the
/// pre-FAN-159 numbers stay reproducible and the gap between the two is
/// measurable, NOT as an alternative way to grade the model: TradeValue is 65%
/// FantasyPros rank (85% for TEs) and is then scored against FantasyPros rank,
/// so this basis is grading the anchor against itself.</item>
/// </list>
///
/// Anything else is rejected rather than defaulted — a typo silently falling
/// back to the flattering basis is exactly the failure this ticket exists to
/// remove.
/// </summary>
public record RunCalibrationCommand(
    int Season,
    string ScoringFormat = "Superflex",
    string ValueBasis = CalibrationValueBasis.Model) : IRequest<RunCalibrationResult>;

public static class CalibrationValueBasis
{
    public const string Model = "Model";
    public const string Blended = "Blended";
}

public record RunCalibrationResult(
    double SpearmanRho,
    double AvgAbsDelta,
    int Top10Overlap,
    int PlayerCount,
    List<CalibrationPlayerSnapshot> Top20Snapshot,
    int UnmatchedCount = 0,
    List<string>? TopUnmatched = null,
    List<CalibrationPlayerSnapshot>? Worst20Snapshot = null,
    string ValueBasis = CalibrationValueBasis.Model);

public class RunCalibrationCommandHandler(
    IDynastyValuationRepository valuationRepo,
    IFantasyProsRookieRankingRepository fpRookieRepo,
    ICalibrationResultRepository calibrationRepo) : IRequestHandler<RunCalibrationCommand, RunCalibrationResult>
{
    public async Task<RunCalibrationResult> Handle(RunCalibrationCommand request, CancellationToken ct)
    {
        // ── FAN-159: which of our two values is being graded ──────────────
        //
        // Until 2026-09-07 this was unconditionally TradeValue, and the metrics
        // below were scored against FantasyPros rank. TradeValue is blended 65%
        // toward an anchor that is a pure step function of FantasyPros rank
        // (85% for tight ends), so the harness was grading FantasyPros against
        // itself at 65% weight: a model producing noise would still have posted
        // a substantial ρ, because the blend alone supplied most of the
        // ordering. Every figure this harness has ever produced — including the
        // 0.8572 that "cleared the target" on 2026-09-06 — was measuring the
        // blend, not the model.
        //
        // ModelValue is the same pipeline with the FP-derived steps removed.
        // Ranking on it is what makes ρ mean what its name says.
        var basis = request.ValueBasis;
        if (basis != CalibrationValueBasis.Model && basis != CalibrationValueBasis.Blended)
            throw new ArgumentException(
                $"Unknown calibration value basis '{basis}'. Expected " +
                $"'{CalibrationValueBasis.Model}' or '{CalibrationValueBasis.Blended}'.",
                nameof(request));

        var useModelValue = basis == CalibrationValueBasis.Model;

        // Select on the same basis we rank on. Taking the top 250 by TradeValue
        // and then re-sorting them by ModelValue would drop every player the FP
        // blend pushed out of the top 250 — precisely the players where model
        // and consensus disagree most — biasing the sample toward agreement by
        // the same mechanism this change exists to remove.
        var ourValuations = useModelValue
            ? await valuationRepo.GetTopByModelValueAsync(250, position: null, ct)
            : await valuationRepo.GetTopByTradeValueAsync(250, position: null, ct);

        double OurValue(DynastyValuationDocument v) => useModelValue ? v.ModelValue : v.TradeValue;

        // A collection written before ModelValue existed reads as 0 for every
        // row. Ranking on a constant would produce a real-looking ρ off nothing
        // but insertion order, so say what happened instead.
        if (useModelValue && ourValuations.TrueForAll(v => v.ModelValue <= 0))
            throw new InvalidOperationException(
                "No valuations carry a ModelValue. Re-run the DFV calculation " +
                "(POST /api/v1/admin/jobs/run-dfv) — ModelValue is only written by " +
                "runs from 2026-09-07 onward. To measure the previously-reported " +
                "blended numbers instead, run with ValueBasis 'Blended'.");

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
            .OrderByDescending(OurValue)
            .Select((v, idx) => new
            {
                OurRank = idx + 1,
                v.PlayerName,
                v.Position,
                OurValue = OurValue(v),
                FpRank = fpBySleeperIdRank[v.SleeperPlayerId]
            })
            .ToList();

        var unmatched = ourValuations
            .Where(v => !fpBySleeperIdRank.ContainsKey(v.SleeperPlayerId))
            .OrderByDescending(OurValue)
            .ToList();

        var valueLabel = useModelValue ? "MV" : "TV";
        var topUnmatched = unmatched
            .Take(10)
            .Select(v => $"{v.PlayerName} ({v.Position}, {valueLabel} {Math.Round(OurValue(v), 1)})")
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
            int ourRank, string playerName, string position, double ourValue, int fpRank)
        {
            var fpSubsetRank = fpDenseRankBySubsetPosition[fpRank];
            return new CalibrationPlayerSnapshot
            {
                OurRank = ourRank,
                PlayerName = playerName,
                Position = position,
                // Whichever value this run ranked on — ModelValue or TradeValue.
                // The property keeps its name because it is persisted and read by
                // the Admin page; ValueBasis on the parent document says which.
                OurTradeValue = Math.Round(ourValue, 1),
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
            .Select(p => MakeSnapshot(p.OurRank, p.PlayerName, p.Position, p.OurValue, p.FpRank))
            .ToList();

        // The twenty biggest disagreements anywhere in the population. This is the
        // view that actually points at the error: the top-20 table above sits
        // comfortably inside the Avg |Δ| target while the tail is three times
        // worse, so the headline metric can only be moved from down here.
        var worstSnapshot = subset
            .OrderByDescending(p => Math.Abs(p.OurRank - fpDenseRankBySubsetPosition[p.FpRank]))
            .ThenBy(p => p.OurRank)
            .Take(20)
            .Select(p => MakeSnapshot(p.OurRank, p.PlayerName, p.Position, p.OurValue, p.FpRank))
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
            TopUnmatched = topUnmatched,
            ValueBasis = basis
        };

        await calibrationRepo.InsertAsync(doc, ct);

        return new RunCalibrationResult(
            doc.SpearmanRho, doc.AvgAbsDelta, top10Overlap, n, snapshot,
            unmatched.Count, topUnmatched, worstSnapshot, basis);
    }
}
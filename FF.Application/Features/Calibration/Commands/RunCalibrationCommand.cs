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
/// <item><c>Raw</c> (FAN-166) — <c>RawValue</c>, the pipeline after rank
/// normalization and before the positional guardrail caps. The difference
/// between this and <c>Model</c> from the same run is what the cap tables
/// contribute to ρ, which nobody had a number for.</item>
/// <item><c>Model</c> (default) — <c>ModelValue</c>, the pipeline with every
/// FantasyPros-derived step removed but our own guardrails applied.</item>
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
///
/// <para>
/// FAN-166, on why <c>Raw</c> was added. FAN-159 kept the guardrails on the
/// model track on the grounds that they are ours rather than FantasyPros'. That
/// is true of their provenance and misleading about their effect: the tier
/// tables were fitted against the blended distribution, so applying them to the
/// model's own uncorrected rank order lands them somewhere else entirely, and
/// they are the only stage in the pipeline that partitions by position and can
/// therefore reorder across positions. Measured 2026-09-08, raw DFV has
/// A.J. Brown at 428 and Jeremy Ruckert at 195 while ModelValue has Ruckert
/// ahead. Run all three bases and the question stops being a judgement call.
/// </para>
/// </summary>
/// <remarks>
/// FAN-166. <paramref name="Position"/> restricts the run to one position
/// ("QB", "RB", "WR", "TE"); null runs the whole board.
///
/// <para>
/// A whole-board ρ answers two questions at once — is the cross-position ladder
/// right, and is the ordering inside each position right — and on 2026-09-08
/// those two had opposite signs. Raw scored 0.8047 against Model's 0.5875 while
/// putting Blake Bortles, Bailey Zappe and Stetson Bennett in the top 15% of the
/// board, because the FantasyPros list is Superflex and a QB-first ordering
/// correlates with it for a reason that has nothing to do with ranking well.
/// </para>
///
/// <para>
/// Restricting to a position removes the ladder from the comparison: both series
/// are dense-ranked within the matched subset, so only within-position ordering
/// is left. Run a basis with and without a position and the difference is the
/// ladder's contribution.
/// </para>
/// </remarks>
public record RunCalibrationCommand(
    int Season,
    string ScoringFormat = "Superflex",
    string ValueBasis = CalibrationValueBasis.Model,
    string? Position = null) : IRequest<RunCalibrationResult>;

public static class CalibrationValueBasis
{
    public const string Raw = "Raw";
    public const string Model = "Model";
    public const string Blended = "Blended";

    public static readonly string[] All = [Raw, Model, Blended];

    public static bool IsKnown(string basis) =>
        All.Contains(basis, StringComparer.Ordinal);
}

/// <param name="SelectedCount">
/// FAN-175. How many valuations the selection actually returned, against the
/// <c>requested</c> top-N. These were assumed equal and were not: the selection
/// queries took the top 250 with no scored filter, so a position with fewer than
/// 250 scored players had its population padded out of the zeroed tail. Reported
/// so the gap can never be invisible again.
/// </param>
/// <param name="RequestedCount">The top-N the selection asked for.</param>
/// <param name="MatchedPlayerIds">
/// FAN-175. The exact players this run graded, in ranked order. Two runs' rho are
/// comparable only if this list is, and until now nothing recorded it — so every
/// within-position comparison this project has made rested on an assumption that
/// was never checkable, and turned out to be false. Persisted with the result so
/// the check is a set difference rather than an argument.
/// </param>
public record RunCalibrationResult(
    double SpearmanRho,
    double AvgAbsDelta,
    int Top10Overlap,
    int PlayerCount,
    List<CalibrationPlayerSnapshot> Top20Snapshot,
    int UnmatchedCount = 0,
    List<string>? TopUnmatched = null,
    List<CalibrationPlayerSnapshot>? Worst20Snapshot = null,
    string ValueBasis = CalibrationValueBasis.Model,
    string? Position = null,
    int SelectedCount = 0,
    int RequestedCount = 0,
    List<string>? MatchedPlayerIds = null);

public class RunCalibrationCommandHandler(
    IDynastyValuationRepository valuationRepo,
    IFantasyProsRookieRankingRepository fpRookieRepo,
    ICalibrationResultRepository calibrationRepo) : IRequestHandler<RunCalibrationCommand, RunCalibrationResult>
{
    // FAN-166. The positions the valuation pipeline models. Deliberately a local
    // list rather than a reach into FF.Domain's aging-curve window table: those
    // happen to hold the same four strings today, but they answer different
    // questions and coupling them would make a change to one silently reshape
    // the other.
    private static readonly string[] ModelledPositions = ["QB", "RB", "WR", "TE"];


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
        if (!CalibrationValueBasis.IsKnown(basis))
            throw new ArgumentException(
                $"Unknown calibration value basis '{basis}'. Expected one of " +
                $"{string.Join(", ", CalibrationValueBasis.All.Select(b => $"'{b}'"))}.",
                nameof(request));

        // Select on the same basis we rank on. Taking the top 250 by TradeValue
        // and then re-sorting them by ModelValue would drop every player the FP
        // blend pushed out of the top 250 — precisely the players where model
        // and consensus disagree most — biasing the sample toward agreement by
        // the same mechanism this change exists to remove. FAN-166 adds a third
        // basis and the rule is unchanged: the guardrail caps move players in
        // and out of the top 250 too, and those are the population Raw exists
        // to look at.
        // FAN-166: null means the whole board, which is the historical behaviour.
        // A named position is validated rather than passed through — a typo would
        // otherwise match nothing, fail the n < 10 guard, and report as "not
        // enough matched players" rather than "that is not a position".
        var position = string.IsNullOrWhiteSpace(request.Position) ? null : request.Position.Trim().ToUpperInvariant();
        if (position is not null && !ModelledPositions.Contains(position))
            throw new ArgumentException(
                $"Unknown position '{request.Position}'. Expected one of " +
                $"{string.Join(", ", ModelledPositions.Select(p => $"'{p}'"))}, or none for the whole board.",
                nameof(request));

        // FAN-175. Named rather than inlined, because the gap between what this
        // asks for and what it gets is the defect: the selection returned 250 rows
        // for every position while only 115 QBs, 189 RBs and 201 TEs had been
        // scored, and the difference was zeroed players graded as if they were
        // ranked last. The queries now filter on IsScored, so this can legitimately
        // come back short — and when it does, that is the real population and it is
        // reported rather than topped up.
        const int RequestedCount = 250;

        var ourValuations = basis switch
        {
            CalibrationValueBasis.Raw => await valuationRepo.GetTopByRawValueAsync(RequestedCount, position, ct),
            CalibrationValueBasis.Model => await valuationRepo.GetTopByModelValueAsync(RequestedCount, position, ct),
            _ => await valuationRepo.GetTopByTradeValueAsync(RequestedCount, position, ct)
        };

        double OurValue(DynastyValuationDocument v) => basis switch
        {
            CalibrationValueBasis.Raw => v.RawValue,
            CalibrationValueBasis.Model => v.ModelValue,
            _ => v.TradeValue
        };

        // FAN-175. IsScored is written by DFV runs from 2026-09-10 onward, and a
        // row without the field never matches the filter — so a collection last
        // written by an older run selects nothing at all. That is the intended
        // failure: the alternative, which is what shipped before, was to return a
        // population padded with players nobody had valued and report a number for
        // it. Named separately from the all-zero guard below because the remedy is
        // the same command but the cause is not.
        if (ourValuations.Count == 0)
            throw new InvalidOperationException(
                "No scored valuations were selected"
                + (position is null ? "" : $" for position {position}")
                + ". Re-run the DFV calculation (POST /api/v1/admin/jobs/run-dfv) — the "
                + "IsScored flag this selection filters on is only written by runs from "
                + "2026-09-10 onward, and before it existed this query silently padded "
                + "its population with zeroed players (FAN-175).");

        // A collection written before the selected field existed reads as 0 for
        // every row. Ranking on a constant would produce a real-looking ρ off
        // nothing but insertion order, so say what happened instead.
        if (basis != CalibrationValueBasis.Blended && ourValuations.TrueForAll(v => OurValue(v) <= 0))
        {
            var writtenFrom = basis == CalibrationValueBasis.Raw ? "2026-09-08" : "2026-09-07";
            throw new InvalidOperationException(
                $"No valuations carry a {basis}Value. Re-run the DFV calculation " +
                $"(POST /api/v1/admin/jobs/run-dfv) — {basis}Value is only written by " +
                $"runs from {writtenFrom} onward. To measure the previously-reported " +
                "blended numbers instead, run with ValueBasis 'Blended'.");
        }

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
                v.SleeperPlayerId,
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

        var valueLabel = basis switch
        {
            CalibrationValueBasis.Raw => "RV",
            CalibrationValueBasis.Model => "MV",
            _ => "TV"
        };
        var topUnmatched = unmatched
            .Take(10)
            .Select(v => $"{v.PlayerName} ({v.Position}, {valueLabel} {Math.Round(OurValue(v), 1)})")
            .ToList();

        int n = Math.Min(matched.Count, 200);
        if (n < 10)
            throw new InvalidOperationException(
                $"Only {n} players matched between our valuations and FP rankings" +
                (position is null ? "" : $" for position {position}") + ". " +
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
            ValueBasis = basis,
            Position = position,

            // FAN-175. The three fields that make a result comparable to another
            // result. SelectedCount against RequestedCount says whether the
            // selection ran out of real players, and MatchedPlayerIds says exactly
            // who was graded — the record whose absence is the whole ticket.
            SelectedCount = ourValuations.Count,
            RequestedCount = RequestedCount,
            MatchedPlayerIds = [.. subset.Select(p => p.SleeperPlayerId)]
        };

        await calibrationRepo.InsertAsync(doc, ct);

        return new RunCalibrationResult(
            doc.SpearmanRho, doc.AvgAbsDelta, top10Overlap, n, snapshot,
            unmatched.Count, topUnmatched, worstSnapshot, basis, position,
            doc.SelectedCount, doc.RequestedCount, doc.MatchedPlayerIds);
    }
}
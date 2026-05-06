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
    List<CalibrationPlayerSnapshot> Top20Snapshot);

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

        // Match our valuations to FP — only players present in both lists
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

        int n = Math.Min(matched.Count, 200);
        if (n < 10)
            throw new InvalidOperationException(
                $"Only {n} players matched between our valuations and FP rankings. " +
                "Run DFV calculation and re-import FP rankings before calibrating.");

        var subset = matched.Take(n).ToList();

        // Spearman ρ — rank by our ordering (already 1..n), compare to FP ranks
        double sumD2 = subset.Sum(p => Math.Pow(p.OurRank - p.FpRank, 2));
        double rho = 1.0 - (6.0 * sumD2) / ((double)n * (n * n - 1));

        // Avg absolute delta
        double avgDelta = subset.Average(p => Math.Abs(p.OurRank - p.FpRank));

        // Top-10 overlap
        var ourTop10SleeperIds = ourValuations.Take(10)
            .Select(v => v.SleeperPlayerId).ToHashSet();
        var fpTop10SleeperIds = fpRankings
            .Where(f => f.FantasyProsRank <= 10 && !string.IsNullOrEmpty(f.SleeperPlayerId))
            .Select(f => f.SleeperPlayerId).ToHashSet();
        int top10Overlap = ourTop10SleeperIds.Intersect(fpTop10SleeperIds).Count();

        // Top-20 snapshot
        var snapshot = subset.Take(20).Select(p => new CalibrationPlayerSnapshot
        {
            OurRank = p.OurRank,
            PlayerName = p.PlayerName,
            Position = p.Position,
            OurTradeValue = Math.Round(p.TradeValue, 1),
            FpRank = p.FpRank,
            Delta = p.OurRank - p.FpRank
        }).ToList();

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
            Top20Snapshot = snapshot
        };

        await calibrationRepo.InsertAsync(doc, ct);

        return new RunCalibrationResult(doc.SpearmanRho, doc.AvgAbsDelta, top10Overlap, n, snapshot);
    }
}
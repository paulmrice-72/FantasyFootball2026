using FF.Application.Features.Dynasty.Commands;
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Services;

public interface ITradeAnalyzerService
{
    Task<TradeAnalysisDocument> AnalyzeAsync(
        string userId,
        IEnumerable<string> myPlayerSleeperIds,
        IEnumerable<string> theirPlayerSleeperIds,
        IEnumerable<TradePickRequest> myPicks,
        IEnumerable<TradePickRequest> theirPicks,
        int season,
        CancellationToken ct = default);
}
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Services;

public interface ITradeAnalyzerService
{
    Task<TradeAnalysisDocument> AnalyzeAsync(
        string userId,
        IEnumerable<string> myPlayerSleeperIds,
        IEnumerable<string> theirPlayerSleeperIds,
        int season,
        CancellationToken ct = default);
}
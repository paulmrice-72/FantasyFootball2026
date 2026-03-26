using FF.Domain.Documents;

namespace FF.Application.Interfaces.Repositories;

public interface ITradeAnalysisRepository
{
    Task<TradeAnalysisDocument?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<List<TradeAnalysisDocument>> GetByUserIdAsync(string userId, CancellationToken ct = default);
    Task InsertAsync(TradeAnalysisDocument document, CancellationToken ct = default);
}
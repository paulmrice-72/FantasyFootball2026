using FF.Domain.Documents;
using FF.Domain.Enums;

namespace FF.Application.Interfaces.Repositories;

public interface IDynastyValuationRepository
{
    Task<DynastyValuationDocument?> GetBySleeperIdAsync(string sleeperPlayerId, CancellationToken ct = default);
    Task<List<DynastyValuationDocument>> GetByPositionAsync(string position, CancellationToken ct = default);
    Task<List<DynastyValuationDocument>> GetTopByTradeValueAsync(int count, string? position = null, CancellationToken ct = default);
    Task UpsertAsync(DynastyValuationDocument document, CancellationToken ct = default);
    Task UpsertBatchAsync(IEnumerable<DynastyValuationDocument> documents, CancellationToken ct = default);

    // ── E10 Dynasty Draft ─────────────────────────────────────────────────
    Task<List<DynastyValuationDocument>> GetBySleeperPlayerIdsAsync(
        IEnumerable<string> sleeperPlayerIds, CancellationToken ct = default);
}
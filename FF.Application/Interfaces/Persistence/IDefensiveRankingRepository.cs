using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence
{
    public interface IDefensiveRankingRepository
    {
        Task UpsertAsync(DefensiveRankingDocument document, CancellationToken ct = default);
        Task UpsertBatchAsync(IEnumerable<DefensiveRankingDocument> documents, CancellationToken ct = default);
        Task<DefensiveRankingDocument?> GetAsync(string team, string position, int season, int week, CancellationToken ct = default);
        Task<List<DefensiveRankingDocument>> GetByWeekAsync(int season, int week, CancellationToken ct = default);
        Task EnsureIndexesAsync();
    }
}
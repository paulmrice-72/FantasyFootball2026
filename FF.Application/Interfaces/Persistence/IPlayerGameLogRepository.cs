using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IPlayerGameLogRepository
{
    Task<Dictionary<int, long>> GetDocumentCountsBySeasonAsync(
        CancellationToken cancellationToken = default);

    Task<(int Inserted, int Replaced)> UpsertBatchAsync(
        IEnumerable<PlayerGameLogDocument> documents,
        CancellationToken cancellationToken = default);

    Task<List<PlayerGameLogDocument>> GetPlayerGameLogsAsync(
        string playerId,
        IEnumerable<int> seasons,
        CancellationToken cancellationToken = default);

    Task<List<PlayerGameLogDocument>> GetWeeklyLogsAsync(
        int season,
        int week,
        CancellationToken cancellationToken = default);

    Task<List<PlayerGameLogDocument>> GetByPlayerSeasonAsync(
    string playerId,
    int season,
    CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> GetDistinctPlayerIdsAsync(
        int season, CancellationToken ct = default);

    Task<long> DeleteSeasonAsync(
        int season,
        CancellationToken cancellationToken = default);

    Task<List<PlayerGameLogDocument>> GetDocumentsWithNullSleeperIdAsync(
    CancellationToken cancellationToken = default);

    Task UpdateSleeperPlayerIdAsync(
        string playerId,
        string sleeperPlayerId,
        CancellationToken cancellationToken = default);

    Task<List<PlayerGameLogDocument>> GetBySeasonAsync(
    int season,
    CancellationToken cancellationToken = default);

    Task BulkUpdateSnapCountsAsync(
        IEnumerable<PlayerGameLogDocument> documents,
        CancellationToken cancellationToken = default);

    Task EnsureIndexesAsync();

    Task<IReadOnlyList<PlayerGameLogDocument>> GetRecentAsync(
        string playerId, int season, int currentWeek, int lookbackWeeks, CancellationToken ct = default);

    Task<PlayerGameLogDocument?> GetMostRecentAsync(
        string playerId, int season, int beforeWeek, CancellationToken ct = default);
}
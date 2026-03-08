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

    Task<long> DeleteSeasonAsync(
        int season,
        CancellationToken cancellationToken = default);

    Task EnsureIndexesAsync();
}
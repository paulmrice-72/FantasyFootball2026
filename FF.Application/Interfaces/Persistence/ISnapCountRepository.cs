// FF.Application/Interfaces/Persistence/ISnapCountRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface ISnapCountRepository
{
    Task<(int Inserted, int Replaced)> UpsertBatchAsync(
        IEnumerable<SnapCountDocument> documents,
        CancellationToken cancellationToken = default);

    Task<List<SnapCountDocument>> GetBySeasonWeekAsync(
        int season,
        int week,
        CancellationToken cancellationToken = default);

    Task EnsureIndexesAsync();
}
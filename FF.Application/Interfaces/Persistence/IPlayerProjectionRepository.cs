// FF.Application/Interfaces/IPlayerProjectionRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IPlayerProjectionRepository
{
    Task UpsertAsync(PlayerProjectionDocument projection, CancellationToken ct = default);
    Task UpsertBatchAsync(IEnumerable<PlayerProjectionDocument> projections, CancellationToken ct = default);
    Task<IReadOnlyList<PlayerProjectionDocument>> GetByWeekAsync(int season, int week, CancellationToken ct = default);
    Task<IReadOnlyList<PlayerProjectionDocument>> GetByPositionAsync(int season, int week, string position, CancellationToken ct = default);
    Task<PlayerProjectionDocument?> GetByPlayerAsync(string playerId, int season, int week, CancellationToken ct = default);
}
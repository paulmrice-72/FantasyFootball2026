// FF.Application/Interfaces/Persistence/IFantasyProsRookieRankingRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IFantasyProsRookieRankingRepository
{
    Task<IReadOnlyList<FantasyProsRookieRankingDocument>> GetBySleeperPlayerIdsAsync(
        IEnumerable<string> sleeperPlayerIds,
        CancellationToken cancellationToken = default);

    Task UpsertManyAsync(IEnumerable<FantasyProsRookieRankingDocument> documents,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<FantasyProsRookieRankingDocument>> GetAllBySeasonAsync(
            int season, CancellationToken cancellationToken = default);

    Task UpsertAsync(FantasyProsRookieRankingDocument document, CancellationToken cancellationToken = default);
}
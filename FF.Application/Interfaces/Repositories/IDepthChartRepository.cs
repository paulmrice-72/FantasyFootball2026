// FF.Application/Interfaces/Repositories/IDepthChartRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Repositories;

public interface IDepthChartRepository
{
    Task UpsertBatchAsync(IReadOnlyList<DepthChartDocument> rows, CancellationToken ct = default);

    Task<IReadOnlyList<DepthChartDocument>> GetByTeamAsync(
        string nflTeam, int season, int week, CancellationToken ct = default);

    Task<IReadOnlyList<DepthChartDocument>> GetByPlayerAsync(
        string sleeperPlayerId, int season, CancellationToken ct = default);

    /// <summary>
    /// Bulk-loads the most recent depth chart entry for each of the given players.
    /// Returns one document per player (the highest-week entry for the season).
    /// Used by grading handlers to apply depth penalties without N+1 queries.
    /// </summary>
    Task<IReadOnlyList<DepthChartDocument>> GetLatestBySleeperIdsAsync(
        IReadOnlyList<string> sleeperPlayerIds, int season, CancellationToken ct = default);
}
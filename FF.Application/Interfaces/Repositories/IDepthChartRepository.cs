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

    /// <summary>
    /// Bulk-loads the most recent depth chart entry for every player at a
    /// position, without needing the caller to know the ids up front.
    ///
    /// Added 2026-09-07 for the projection role gate. Pass 1 of
    /// CalculateProjectionsCommandHandler iterates GSIS ids and only learns a
    /// player's Sleeper id from his game log inside the loop, so
    /// <see cref="GetLatestBySleeperIdsAsync"/> cannot be used to preload —
    /// the alternative was a per-player query inside the loop, which is the
    /// N+1 that method exists to avoid.
    /// </summary>
    Task<IReadOnlyList<DepthChartDocument>> GetLatestByPositionAsync(
        string position, int season, CancellationToken ct = default);
}
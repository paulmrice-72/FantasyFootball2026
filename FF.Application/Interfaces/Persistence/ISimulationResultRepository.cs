using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface ISimulationResultRepository
{
    Task UpsertAsync(SimulationResultDocument document, CancellationToken ct = default);
    Task UpsertBatchAsync(IEnumerable<SimulationResultDocument> documents, CancellationToken ct = default);
    Task<SimulationResultDocument?> GetByPlayerAsync(string playerId, int season, int week, CancellationToken ct = default);
    Task<IReadOnlyList<SimulationResultDocument>> GetByWeekAsync(int season, int week, CancellationToken ct = default);
    Task<IReadOnlyList<SimulationResultDocument>> GetByPositionAsync(int season, int week, string position, CancellationToken ct = default);
    Task<SimulationResultDocument?> GetMostRecentBySleeperIdAsync(string sleeperPlayerId, int season, CancellationToken ct = default);
    Task<IReadOnlyList<SimulationResultDocument>> GetLatestBySleeperIdsAsync(IEnumerable<string> sleeperPlayerIds, int season, CancellationToken ct = default);

    /// <summary>
    /// Fallback lookup by player name + position when SleeperPlayerId → GSIS bridge
    /// is missing (e.g. 2025 rookies whose GSIS IDs aren't yet in Sleeper).
    /// Returns the season-average (Week=0) result for the most recent available season.
    /// </summary>
    Task<SimulationResultDocument?> GetMostRecentByNameAsync(string playerName, string position, int season, CancellationToken ct = default);
}
// FF.Application/Interfaces/Persistence/ISimulationResultRepository.cs
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
}
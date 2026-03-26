using FF.Domain.Documents;

namespace FF.Application.Interfaces.Repositories;

public interface ICareerSimulationRepository
{
    Task<CareerSimulationDocument?> GetByPlayerIdAsync(string sleeperPlayerId, CancellationToken ct = default);
    Task<List<CareerSimulationDocument>> GetByPositionAsync(string position, CancellationToken ct = default);
    Task UpsertAsync(CareerSimulationDocument document, CancellationToken ct = default);
    Task UpsertBatchAsync(IEnumerable<CareerSimulationDocument> documents, CancellationToken ct = default);
}
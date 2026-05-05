using FF.Domain.Documents;

namespace FF.Application.Interfaces.Repositories;

public interface ICareerSimulationRepository
{
    Task<CareerSimulationDocument?> GetByPlayerIdAsync(string sleeperPlayerId, CancellationToken ct = default);
    Task<List<CareerSimulationDocument>> GetByPositionAsync(string position, CancellationToken ct = default);

    /// <summary>
    /// Loads all career simulations for a given season in a single query.
    /// Use this instead of per-player GetByPlayerIdAsync calls in bulk
    /// calculation jobs — eliminates N serial round-trips to MongoDB.
    /// </summary>
    Task<List<CareerSimulationDocument>> GetAllBySeasonAsync(int season, CancellationToken ct = default);

    Task UpsertAsync(CareerSimulationDocument document, CancellationToken ct = default);
    Task UpsertBatchAsync(IEnumerable<CareerSimulationDocument> documents, CancellationToken ct = default);
}
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Services;

public interface ICareerSimulationService
{
    Task<CareerSimulationDocument> SimulatePlayerCareerAsync(
        string sleeperPlayerId,
        CancellationToken ct = default);

    Task<List<CareerSimulationDocument>> SimulateAllPlayersAsync(
        int season,
        CancellationToken ct = default);
}
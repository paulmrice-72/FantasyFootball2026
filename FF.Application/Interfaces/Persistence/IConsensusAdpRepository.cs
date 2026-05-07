// FF.Application/Interfaces/Persistence/IConsensusAdpRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IConsensusAdpRepository
{
    Task<ConsensusAdpDocument?> GetBySleeperPlayerIdAsync(
        string sleeperPlayerId,
        CancellationToken cancellationToken = default);

    Task<List<ConsensusAdpDocument>> GetBySleeperPlayerIdsAsync(
        List<string> sleeperPlayerIds,
        CancellationToken cancellationToken = default);

    Task UpsertManyAsync(
        List<ConsensusAdpDocument> documents,
        CancellationToken cancellationToken = default);
}
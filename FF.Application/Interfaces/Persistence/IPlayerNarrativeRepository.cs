// FF.Application/Interfaces/Persistence/IPlayerNarrativeRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IPlayerNarrativeRepository
{
    Task<PlayerNarrativeDocument?> GetBySleeperPlayerIdAsync(
        string sleeperPlayerId, CancellationToken ct = default);

    Task UpsertAsync(
        PlayerNarrativeDocument document, CancellationToken ct = default);
}
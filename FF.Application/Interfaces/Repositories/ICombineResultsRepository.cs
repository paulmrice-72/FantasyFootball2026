// FF.Application/Interfaces/Repositories/ICombineResultRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Repositories;

public interface ICombineResultRepository
{
    Task<List<CombineResultDocument>> GetBySleeperPlayerIdsAsync(
        List<string> sleeperPlayerIds,
        CancellationToken cancellationToken = default);

    Task UpsertManyAsync(
        List<CombineResultDocument> documents,
        CancellationToken cancellationToken = default);
}
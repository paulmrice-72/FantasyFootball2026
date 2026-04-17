// FF.Application/Interfaces/Persistence/IPffDraftGradeRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IPffDraftGradeRepository
{
    Task<List<PffDraftGradeDocument>> GetBySleeperPlayerIdsAsync(
        List<string> sleeperPlayerIds,
        CancellationToken cancellationToken = default);

    Task UpsertManyAsync(
        List<PffDraftGradeDocument> documents,
        CancellationToken cancellationToken = default);
}
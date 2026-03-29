// FF.Application/Interfaces/Persistence/IVorpRecommendationRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IVorpRecommendationRepository
{
    Task UpsertBatchAsync(
        IEnumerable<VorpRecommendationDocument> recommendations,
        CancellationToken ct = default);

    Task<IReadOnlyList<VorpRecommendationDocument>> GetByWeekAsync(
        int season,
        int week,
        string? position = null,
        int top = 50,
        CancellationToken ct = default);
}
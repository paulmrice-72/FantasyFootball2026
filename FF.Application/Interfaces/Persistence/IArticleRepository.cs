// FF.Application/Interfaces/Persistence/IArticleRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IArticleRepository
{
    Task UpsertAsync(ArticleDocument article, CancellationToken ct = default);
    Task<IReadOnlyList<ArticleDocument>> GetBySeasonWeekAsync(int season, int week, CancellationToken ct = default);
    Task<IReadOnlyList<ArticleDocument>> GetLatestAsync(int count = 10, CancellationToken ct = default);
}
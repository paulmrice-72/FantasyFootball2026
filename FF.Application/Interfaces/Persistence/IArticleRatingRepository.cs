// FF.Application/Interfaces/Persistence/IArticleRatingRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IArticleRatingRepository
{
    Task<ArticleRatingDocument?> GetAsync(string articleId, string userId, CancellationToken ct = default);
    Task UpsertAsync(ArticleRatingDocument rating, CancellationToken ct = default);
}
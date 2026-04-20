// FF.Application/Interfaces/Persistence/IArticleRepository.cs
using FF.Domain.Documents;
using FF.Domain.Enums;

namespace FF.Application.Interfaces.Persistence;

public interface IArticleRepository
{
    Task UpsertAsync(ArticleDocument article, CancellationToken ct = default);
    Task<IReadOnlyList<ArticleDocument>> GetBySeasonWeekAsync(int season, int week, CancellationToken ct = default);
    Task<IReadOnlyList<ArticleDocument>> GetLatestAsync(int count = 10, CancellationToken ct = default);
    Task<IReadOnlyList<ArticleDocument>> GetAllForReviewAsync(CancellationToken ct = default);
    Task SetReviewStatusAsync(string id, ArticleReviewStatus status, string reviewedBy, CancellationToken ct = default);
    Task IncrementRatingAsync(string id, bool thumbsUp, CancellationToken ct = default);
    Task DeleteAsync(string id, CancellationToken ct = default);
    Task SetAdminNotesAsync(string id, string notes, string? newTopic, CancellationToken ct = default);
    // Add to IArticleRepository.cs
    Task<IReadOnlyList<ArticleDocument>> GetByPersonaAsync(
        string personaId, int? season = null, int? month = null,
        CancellationToken ct = default);
    Task<ArticleDocument?> GetByIdAsync(string id, CancellationToken ct = default);
}
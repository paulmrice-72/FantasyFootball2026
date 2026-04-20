// FF.Infrastructure/Persistence/Mongo/Repositories/ArticleRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Domain.Enums;
using FF.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class ArticleRepository(MongoDbContext context) : IArticleRepository
{
    private readonly IMongoCollection<ArticleDocument> _collection =
        context.GetCollection<ArticleDocument>("articles");

    public async Task UpsertAsync(ArticleDocument article, CancellationToken ct = default)
    {
        article.GeneratedAt = DateTime.UtcNow;
        var filter = Builders<ArticleDocument>.Filter.Eq(x => x.Id, article.Id);
        var options = new ReplaceOptions { IsUpsert = true };
        await _collection.ReplaceOneAsync(filter, article, options, ct);
    }

    public async Task<IReadOnlyList<ArticleDocument>> GetBySeasonWeekAsync(
        int season, int week, CancellationToken ct = default)
    {
        var filter = Builders<ArticleDocument>.Filter.And(
            Builders<ArticleDocument>.Filter.Eq(x => x.Season, season),
            Builders<ArticleDocument>.Filter.Eq(x => x.Week, week),
            Builders<ArticleDocument>.Filter.Eq(x => x.ReviewStatus, ArticleReviewStatus.Approved));
        return await _collection.Find(filter).SortBy(x => x.PersonaName).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ArticleDocument>> GetLatestAsync(
        int count = 10, CancellationToken ct = default)
    {
        var filter = Builders<ArticleDocument>.Filter
            .Eq(x => x.ReviewStatus, ArticleReviewStatus.Approved);
        return await _collection.Find(filter)
            .SortByDescending(x => x.GeneratedAt)
            .Limit(count)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ArticleDocument>> GetAllForReviewAsync(
        CancellationToken ct = default)
    {
        return await _collection.Find(_ => true)
            .SortByDescending(x => x.GeneratedAt)
            .ToListAsync();  // ← remove ct here
    }

    public async Task SetReviewStatusAsync(
        string id, ArticleReviewStatus status, string reviewedBy, CancellationToken ct = default)
    {
        var filter = Builders<ArticleDocument>.Filter.Eq(x => x.Id, id);
        var update = Builders<ArticleDocument>.Update
            .Set(x => x.ReviewStatus, status)
            .Set(x => x.ReviewedBy, reviewedBy)
            .Set(x => x.ReviewedAt, DateTime.UtcNow);
        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task IncrementRatingAsync(
        string id, bool thumbsUp, CancellationToken ct = default)
    {
        var filter = Builders<ArticleDocument>.Filter.Eq(x => x.Id, id);
        var update = thumbsUp
            ? Builders<ArticleDocument>.Update.Inc(x => x.ThumbsUp, 1)
            : Builders<ArticleDocument>.Update.Inc(x => x.ThumbsDown, 1);
        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task DeleteAsync(string id, CancellationToken ct = default)
    {
        var filter = Builders<ArticleDocument>.Filter.Eq(x => x.Id, id);
        await _collection.DeleteOneAsync(filter, ct);
    }

    public async Task SetAdminNotesAsync(
        string id, string notes, string? newTopic, CancellationToken ct = default)
    {
        var filter = Builders<ArticleDocument>.Filter.Eq(x => x.Id, id);
        var update = Builders<ArticleDocument>.Update
            .Set(x => x.AdminNotes, notes)
            .Set(x => x.NewTopic, newTopic)
            .Set(x => x.ReviewStatus, ArticleReviewStatus.Draft);
        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }

    public async Task<IReadOnlyList<ArticleDocument>> GetByPersonaAsync(
        string personaId, int? season = null, int? month = null,
        CancellationToken ct = default)
    {
        var filters = new List<FilterDefinition<ArticleDocument>>
    {
        Builders<ArticleDocument>.Filter.Eq(x => x.PersonaId, personaId),
        Builders<ArticleDocument>.Filter.Eq(x => x.ReviewStatus, ArticleReviewStatus.Approved)
    };

        if (season.HasValue)
        {
            // Filter by the calendar year of GeneratedAt, not the NFL season field
            var yearStart = new DateTime(season.Value, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var yearEnd = new DateTime(season.Value + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            filters.Add(Builders<ArticleDocument>.Filter.Gte(x => x.GeneratedAt, yearStart));
            filters.Add(Builders<ArticleDocument>.Filter.Lt(x => x.GeneratedAt, yearEnd));
        }

        if (month.HasValue)
        {
            var year = season ?? DateTime.UtcNow.Year;
            var start = new DateTime(year, month.Value, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1);
            filters.Add(Builders<ArticleDocument>.Filter.Gte(x => x.GeneratedAt, start));
            filters.Add(Builders<ArticleDocument>.Filter.Lt(x => x.GeneratedAt, end));
        }

        return await _collection
            .Find(Builders<ArticleDocument>.Filter.And(filters))
            .SortByDescending(x => x.GeneratedAt)
            .ToListAsync(CancellationToken.None);
    }
    public async Task<ArticleDocument?> GetByIdAsync(
        string id, CancellationToken ct = default)
    {
        var filter = Builders<ArticleDocument>.Filter.Eq(x => x.Id, id);
        return await _collection.Find(filter).FirstOrDefaultAsync(CancellationToken.None);
    }
}
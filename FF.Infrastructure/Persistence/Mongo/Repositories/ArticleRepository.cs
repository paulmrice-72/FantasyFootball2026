// FF.Infrastructure/Persistence/Mongo/Repositories/ArticleRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
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
            Builders<ArticleDocument>.Filter.Eq(x => x.IsPublished, true));

        return await _collection
            .Find(filter)
            .SortBy(x => x.PersonaName)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ArticleDocument>> GetLatestAsync(
        int count = 10, CancellationToken ct = default)
    {
        var filter = Builders<ArticleDocument>.Filter.Eq(x => x.IsPublished, true);
        return await _collection
            .Find(filter)
            .SortByDescending(x => x.GeneratedAt)
            .Limit(count)
            .ToListAsync(ct);
    }
}
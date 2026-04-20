// FF.Infrastructure/Persistence/Mongo/Repositories/ArticleRatingRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class ArticleRatingRepository(MongoDbContext context) : IArticleRatingRepository
{
    private readonly IMongoCollection<ArticleRatingDocument> _collection =
        context.GetCollection<ArticleRatingDocument>("article_ratings");

    public async Task<ArticleRatingDocument?> GetAsync(
        string articleId, string userId, CancellationToken ct = default)
    {
        var id = $"{articleId}::{userId}";
        var filter = Builders<ArticleRatingDocument>.Filter.Eq(x => x.Id, id);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task UpsertAsync(ArticleRatingDocument rating, CancellationToken ct = default)
    {
        rating.Id = $"{rating.ArticleId}::{rating.UserId}";
        rating.RatedAt = DateTime.UtcNow;
        var filter = Builders<ArticleRatingDocument>.Filter.Eq(x => x.Id, rating.Id);
        var options = new ReplaceOptions { IsUpsert = true };
        await _collection.ReplaceOneAsync(filter, rating, options, ct);
    }
}
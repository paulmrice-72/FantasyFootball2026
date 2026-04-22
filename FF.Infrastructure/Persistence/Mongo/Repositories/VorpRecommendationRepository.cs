// FF.Infrastructure/Persistence/Mongo/Repositories/VorpRecommendationRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class VorpRecommendationRepository : IVorpRecommendationRepository
{
    private readonly IMongoCollection<VorpRecommendationDocument> _collection;

    public VorpRecommendationRepository(MongoDbContext database)
    {
        _collection = database.GetCollection<VorpRecommendationDocument>("vorp_recommendations");

        var indexKeys = Builders<VorpRecommendationDocument>.IndexKeys
            .Ascending(x => x.PlayerId)
            .Ascending(x => x.Season)
            .Ascending(x => x.Week);

        _collection.Indexes.CreateOne(
            new CreateIndexModel<VorpRecommendationDocument>(indexKeys,
                new CreateIndexOptions { Unique = true }));
    }

    public async Task UpsertBatchAsync(
        IEnumerable<VorpRecommendationDocument> recommendations,
        CancellationToken ct = default)
    {
        foreach (var rec in recommendations)
        {
            var filter = Builders<VorpRecommendationDocument>.Filter.And(
                Builders<VorpRecommendationDocument>.Filter.Eq(x => x.PlayerId, rec.PlayerId),
                Builders<VorpRecommendationDocument>.Filter.Eq(x => x.Season, rec.Season),
                Builders<VorpRecommendationDocument>.Filter.Eq(x => x.Week, rec.Week));

            var update = Builders<VorpRecommendationDocument>.Update
                .Set(x => x.PlayerName, rec.PlayerName)
                .Set(x => x.Position, rec.Position)
                .Set(x => x.NflTeam, rec.NflTeam)
                .Set(x => x.ProjectedPoints, rec.ProjectedPoints)
                .Set(x => x.ReplacementLevel, rec.ReplacementLevel)
                .Set(x => x.Vorp, rec.Vorp)
                .Set(x => x.FloorPoints, rec.FloorPoints)
                .Set(x => x.CeilingPoints, rec.CeilingPoints)
                .Set(x => x.VorpRank, rec.VorpRank)
                .Set(x => x.PositionRank, rec.PositionRank)
                .Set(x => x.ComputedAt, rec.ComputedAt);

            await _collection.UpdateOneAsync(
                filter, update, new UpdateOptions { IsUpsert = true }, CancellationToken.None);
        }
    }

    public async Task<IReadOnlyList<VorpRecommendationDocument>> GetByWeekAsync(
        int season,
        int week,
        string? position = null,
        int top = 50,
        CancellationToken ct = default)
    {
        var filter = Builders<VorpRecommendationDocument>.Filter.And(
            Builders<VorpRecommendationDocument>.Filter.Eq(x => x.Season, season),
            Builders<VorpRecommendationDocument>.Filter.Eq(x => x.Week, week),
            Builders<VorpRecommendationDocument>.Filter.Gt(x => x.Vorp, 0));

        if (!string.IsNullOrEmpty(position))
            filter &= Builders<VorpRecommendationDocument>.Filter
                .Eq(x => x.Position, position);

        return await _collection.Find(filter)
            .SortByDescending(x => x.Vorp)
            .Limit(top)
            .ToListAsync(ct);
    }
}
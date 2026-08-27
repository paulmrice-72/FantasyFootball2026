// FF.Infrastructure/Persistence/Mongo/Repositories/RedraftAdpRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class RedraftAdpRepository(MongoDbContext context) : IRedraftAdpRepository
{
    private readonly IMongoCollection<RedraftAdpCacheDocument> _collection =
        context.GetCollection<RedraftAdpCacheDocument>("redraftAdpCache");

    public async Task<List<RedraftAdpCacheDocument>> GetBySeasonAsync(
        int season,
        string scoringFormat = "ppr",
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<RedraftAdpCacheDocument>.Filter.And(
            Builders<RedraftAdpCacheDocument>.Filter.Eq(d => d.Season, season),
            Builders<RedraftAdpCacheDocument>.Filter.Eq(d => d.ScoringFormat, scoringFormat));

        return await _collection
            .Find(filter)
            .ToListAsync(cancellationToken);
    }
}

// FF.Infrastructure/Persistence/Repositories/PlayerNarrativeRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class PlayerNarrativeRepository(MongoDbContext context)
    : IPlayerNarrativeRepository
{
    private IMongoCollection<PlayerNarrativeDocument> Collection =>
        context.GetCollection<PlayerNarrativeDocument>("player_narratives");

    public async Task<PlayerNarrativeDocument?> GetBySleeperPlayerIdAsync(
        string sleeperPlayerId, CancellationToken ct = default)
    {
        var filter = Builders<PlayerNarrativeDocument>.Filter
            .Eq(x => x.SleeperPlayerId, sleeperPlayerId);

        return await Collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task UpsertAsync(
        PlayerNarrativeDocument document, CancellationToken ct = default)
    {
        var filter = Builders<PlayerNarrativeDocument>.Filter
            .Eq(x => x.SleeperPlayerId, document.SleeperPlayerId);

        var update = Builders<PlayerNarrativeDocument>.Update
            .Set(x => x.FullName, document.FullName)
            .Set(x => x.Position, document.Position)
            .Set(x => x.Narrative, document.Narrative)
            .Set(x => x.GeneratedAt, document.GeneratedAt)
            .Set(x => x.ExpiresAt, document.ExpiresAt);

        await Collection.UpdateOneAsync(
            filter, update,
            new UpdateOptions { IsUpsert = true },
            CancellationToken.None);
    }
}
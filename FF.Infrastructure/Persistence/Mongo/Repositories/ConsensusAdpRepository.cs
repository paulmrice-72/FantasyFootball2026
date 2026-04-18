// FF.Infrastructure/Persistence/Mongo/Repositories/ConsensusAdpRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class ConsensusAdpRepository(MongoDbContext context) : IConsensusAdpRepository
{
    private readonly IMongoCollection<ConsensusAdpDocument> _collection =
        context.GetCollection<ConsensusAdpDocument>("consensus_adp");

    public async Task<List<ConsensusAdpDocument>> GetBySleeperPlayerIdsAsync(
        List<string> sleeperPlayerIds,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<ConsensusAdpDocument>.Filter
            .In(d => d.SleeperPlayerId, sleeperPlayerIds);

        return await _collection
            .Find(filter)
            .ToListAsync(cancellationToken);
    }

    public async Task UpsertManyAsync(
        List<ConsensusAdpDocument> documents,
        CancellationToken cancellationToken = default)
    {
        foreach (var doc in documents)
        {
            var filter = Builders<ConsensusAdpDocument>.Filter
                .Eq(d => d.Id, doc.Id);

            var update = Builders<ConsensusAdpDocument>.Update
                .Set(d => d.SleeperPlayerId, doc.SleeperPlayerId)
                .Set(d => d.PlayerName, doc.PlayerName)
                .Set(d => d.Position, doc.Position)
                .Set(d => d.NflTeam, doc.NflTeam)
                .Set(d => d.Adp, doc.Adp)
                .Set(d => d.AdpRank, doc.AdpRank)
                .Set(d => d.Source, doc.Source)
                .Set(d => d.Season, doc.Season)
                .Set(d => d.ImportedAt, doc.ImportedAt);

            await _collection.UpdateOneAsync(
                filter, update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken);
        }
    }
}
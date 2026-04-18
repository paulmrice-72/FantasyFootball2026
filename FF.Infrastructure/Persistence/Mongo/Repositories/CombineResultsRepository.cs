// FF.Infrastructure/Persistence/Mongo/Repositories/CombineResultRepository.cs
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using FF.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class CombineResultRepository(MongoDbContext context) : ICombineResultRepository
{
    private readonly IMongoCollection<CombineResultDocument> _collection =
        context.GetCollection<CombineResultDocument>("combine_results");

    public async Task<List<CombineResultDocument>> GetBySleeperPlayerIdsAsync(
        List<string> sleeperPlayerIds,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<CombineResultDocument>.Filter
            .In(d => d.SleeperPlayerId, sleeperPlayerIds);
        return await _collection.Find(filter).ToListAsync(cancellationToken);
    }

    public async Task UpsertManyAsync(
        List<CombineResultDocument> documents,
        CancellationToken cancellationToken = default)
    {
        foreach (var doc in documents)
        {
            var filter = Builders<CombineResultDocument>.Filter
                .Eq(d => d.Id, doc.Id);

            var update = Builders<CombineResultDocument>.Update
                .Set(d => d.SleeperPlayerId, doc.SleeperPlayerId)
                .Set(d => d.PlayerName, doc.PlayerName)
                .Set(d => d.Position, doc.Position)
                .Set(d => d.NflTeam, doc.NflTeam)
                .Set(d => d.School, doc.School)
                .Set(d => d.Season, doc.Season)
                .Set(d => d.HeightInches, doc.HeightInches)
                .Set(d => d.WeightLbs, doc.WeightLbs)
                .Set(d => d.FortyYard, doc.FortyYard)
                .Set(d => d.BenchReps, doc.BenchReps)
                .Set(d => d.Vertical, doc.Vertical)
                .Set(d => d.BroadJump, doc.BroadJump)
                .Set(d => d.ConeDrill, doc.ConeDrill)
                .Set(d => d.Shuttle, doc.Shuttle)
                .Set(d => d.SpeedScore, doc.SpeedScore)
                .Set(d => d.AthleticismScore, doc.AthleticismScore)
                .Set(d => d.BirthDate, doc.BirthDate)
                .Set(d => d.SyncedAt, doc.SyncedAt);

            await _collection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
                cancellationToken);
        }
    }
}
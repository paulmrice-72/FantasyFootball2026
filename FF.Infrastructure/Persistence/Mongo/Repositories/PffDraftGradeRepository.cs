// FF.Infrastructure/Persistence/Mongo/Repositories/PffDraftGradeRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class PffDraftGradeRepository(MongoDbContext context) : IPffDraftGradeRepository
{
    private readonly IMongoCollection<PffDraftGradeDocument> _collection =
        context.GetCollection<PffDraftGradeDocument>("pff_draft_grades");

    public async Task<PffDraftGradeDocument?> GetBySleeperPlayerIdAsync(
        string sleeperPlayerId,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<PffDraftGradeDocument>.Filter
            .Eq(d => d.SleeperPlayerId, sleeperPlayerId);

        return await _collection
            .Find(filter)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<PffDraftGradeDocument>> GetBySleeperPlayerIdsAsync(
        List<string> sleeperPlayerIds,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<PffDraftGradeDocument>.Filter
            .In(d => d.SleeperPlayerId, sleeperPlayerIds);

        return await _collection
            .Find(filter)
            .ToListAsync(cancellationToken);
    }

    public async Task UpsertManyAsync(
        List<PffDraftGradeDocument> documents,
        CancellationToken cancellationToken = default)
    {
        foreach (var doc in documents)
        {
            var filter = Builders<PffDraftGradeDocument>.Filter
                .Eq(d => d.Id, doc.Id);

            var update = Builders<PffDraftGradeDocument>.Update
                .Set(d => d.SleeperPlayerId, doc.SleeperPlayerId)
                .Set(d => d.PlayerName, doc.PlayerName)
                .Set(d => d.Position, doc.Position)
                .Set(d => d.NflTeam, doc.NflTeam)
                .Set(d => d.PffGrade, doc.PffGrade)
                .Set(d => d.PffRank, doc.PffRank)
                .Set(d => d.Season, doc.Season)
                .Set(d => d.ImportedAt, doc.ImportedAt);

            await _collection.UpdateOneAsync(
                filter, update,
                new UpdateOptions { IsUpsert = true },
                CancellationToken.None);
        }
    }
}
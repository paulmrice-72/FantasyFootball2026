// FF.Infrastructure/Persistence/Mongo/Repositories/DraftSessionRepository.cs
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class DraftSessionRepository(MongoDbContext context) : IDraftSessionRepository
{
    private readonly IMongoCollection<DraftSessionDocument> _collection =
        context.Database.GetCollection<DraftSessionDocument>("draft_sessions");

    public async Task<DraftSessionDocument?> GetByIdAsync(
        string id, CancellationToken ct = default)
    {
        var filter = Builders<DraftSessionDocument>.Filter.Eq(x => x.Id, id);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<DraftSessionDocument?> GetActiveByUserAndLeagueAsync(
        string userId, string leagueId, CancellationToken ct = default)
    {
        var filter = Builders<DraftSessionDocument>.Filter.And(
            Builders<DraftSessionDocument>.Filter.Eq(x => x.UserId, userId),
            Builders<DraftSessionDocument>.Filter.Eq(x => x.LeagueId, leagueId),
            Builders<DraftSessionDocument>.Filter.Eq(x => x.IsActive, true));

        return await _collection.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<DraftSessionDocument>> GetByUserIdAsync(
        string userId, CancellationToken ct = default)
    {
        var filter = Builders<DraftSessionDocument>.Filter.Eq(x => x.UserId, userId);
        return await _collection.Find(filter)
            .SortByDescending(x => x.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task InsertAsync(
        DraftSessionDocument document, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(document.Id))
            document.Id = ObjectId.GenerateNewId().ToString();
        await _collection.InsertOneAsync(document, cancellationToken: ct);
    }

    public async Task UpdateAsync(
        DraftSessionDocument document, CancellationToken ct = default)
    {
        document.UpdatedAt = DateTime.UtcNow;

        var filter = Builders<DraftSessionDocument>.Filter.Eq(x => x.Id, document.Id);
        var update = Builders<DraftSessionDocument>.Update
            .Set(x => x.IsActive, document.IsActive)
            .Set(x => x.Picks, document.Picks)
            .Set(x => x.UpdatedAt, document.UpdatedAt);

        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }
}
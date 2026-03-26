using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class TradeAnalysisRepository(MongoDbContext context) : ITradeAnalysisRepository
{
    private readonly IMongoCollection<TradeAnalysisDocument> _collection =
        context.Database.GetCollection<TradeAnalysisDocument>("trade_analyses");

    public async Task<TradeAnalysisDocument?> GetByIdAsync(
        string id, CancellationToken ct = default)
    {
        var filter = Builders<TradeAnalysisDocument>.Filter.Eq(x => x.Id, id);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<List<TradeAnalysisDocument>> GetByUserIdAsync(
        string userId, CancellationToken ct = default)
    {
        var filter = Builders<TradeAnalysisDocument>.Filter.Eq(x => x.UserId, userId);
        return await _collection.Find(filter)
            .SortByDescending(x => x.AnalyzedAt)
            .ToListAsync(ct);
    }

    public async Task InsertAsync(
        TradeAnalysisDocument document, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(document.Id))
            document.Id = ObjectId.GenerateNewId().ToString();
        await _collection.InsertOneAsync(document, cancellationToken: ct);
    }
}
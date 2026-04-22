using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class DynastyValuationRepository(MongoDbContext context) : IDynastyValuationRepository
{
    private readonly IMongoCollection<DynastyValuationDocument> _collection =
        context.Database.GetCollection<DynastyValuationDocument>("dynasty_valuations");

    public async Task<DynastyValuationDocument?> GetBySleeperIdAsync(
        string sleeperPlayerId, CancellationToken ct = default)
    {
        var filter = Builders<DynastyValuationDocument>.Filter
            .Eq(x => x.SleeperPlayerId, sleeperPlayerId);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<List<DynastyValuationDocument>> GetByPositionAsync(
        string position, CancellationToken ct = default)
    {
        var filter = Builders<DynastyValuationDocument>.Filter
            .Eq(x => x.Position, position);
        return await _collection.Find(filter)
            .SortByDescending(x => x.TradeValue)
            .ToListAsync(ct);
    }

    public async Task<List<DynastyValuationDocument>> GetTopByTradeValueAsync(
        int count, string? position = null, CancellationToken ct = default)
    {
        var filter = position is null
            ? Builders<DynastyValuationDocument>.Filter.Empty
            : Builders<DynastyValuationDocument>.Filter.Eq(x => x.Position, position);

        return await _collection.Find(filter)
            .SortByDescending(x => x.TradeValue)
            .Limit(count)
            .ToListAsync(ct);
    }

    public async Task UpsertAsync(DynastyValuationDocument document, CancellationToken ct = default)
    {
        var filter = Builders<DynastyValuationDocument>.Filter
            .Eq(x => x.SleeperPlayerId, document.SleeperPlayerId);

        var update = Builders<DynastyValuationDocument>.Update
            .Set(x => x.PlayerId, document.PlayerId)
            .Set(x => x.PlayerName, document.PlayerName)
            .Set(x => x.Position, document.Position)
            .Set(x => x.NflTeam, document.NflTeam)
            .Set(x => x.Age, document.Age)
            .Set(x => x.YearsExperience, document.YearsExperience)
            .Set(x => x.Season, document.Season)
            .Set(x => x.BreakoutScore, document.BreakoutScore)
            .Set(x => x.BreakoutClassification, document.BreakoutClassification)
            .Set(x => x.BreakoutSignals, document.BreakoutSignals)
            .Set(x => x.BreakoutScoredAt, document.BreakoutScoredAt)
            .Set(x => x.TradeValue, document.TradeValue)
            .Set(x => x.DiscountedFutureValue, document.DiscountedFutureValue)
            .Set(x => x.CareerValueScore, document.CareerValueScore)
            .Set(x => x.PeakYear, document.PeakYear)
            .Set(x => x.YearsOfPrimeRemaining, document.YearsOfPrimeRemaining)
            .Set(x => x.CareerPhase, document.CareerPhase)
            .SetOnInsert(x => x.Id, document.Id);

        await _collection.UpdateOneAsync(filter, update,
            new UpdateOptions { IsUpsert = true }, CancellationToken.None);
    }

    public async Task UpsertBatchAsync(
        IEnumerable<DynastyValuationDocument> documents, CancellationToken ct = default)
    {
        foreach (var document in documents)
            await UpsertAsync(document, ct);
    }

    public async Task<List<DynastyValuationDocument>> GetBySleeperPlayerIdsAsync(
    IEnumerable<string> sleeperPlayerIds,
    CancellationToken ct = default)
    {
        var filter = Builders<DynastyValuationDocument>.Filter
            .In(x => x.SleeperPlayerId, sleeperPlayerIds);

        return await _collection
            .Find(filter)
            .ToListAsync(ct);
    }
}
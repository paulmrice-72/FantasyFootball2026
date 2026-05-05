// FF.Infrastructure/Persistence/Mongo/Repositories/DynastyValuationRepository.cs
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
        string sleeperPlayerId,
        CancellationToken ct = default)
    {
        var filter = Builders<DynastyValuationDocument>.Filter
            .Eq(x => x.SleeperPlayerId, sleeperPlayerId);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<List<DynastyValuationDocument>> GetByPositionAsync(
        string position,
        CancellationToken ct = default)
    {
        var filter = Builders<DynastyValuationDocument>.Filter
            .Eq(x => x.Position, position);
        return await _collection.Find(filter)
            .SortByDescending(x => x.TradeValue)
            .ToListAsync(ct);
    }

    public async Task<List<DynastyValuationDocument>> GetTopByTradeValueAsync(
        int count,
        string? position = null,
        CancellationToken ct = default)
    {
        var filter = position is null
            ? Builders<DynastyValuationDocument>.Filter.Empty
            : Builders<DynastyValuationDocument>.Filter.Eq(x => x.Position, position);

        return await _collection.Find(filter)
            .SortByDescending(x => x.TradeValue)
            .Limit(count)
            .ToListAsync(ct);
    }

    public async Task UpsertAsync(
        DynastyValuationDocument document,
        CancellationToken ct = default)
    {
        var filter = Builders<DynastyValuationDocument>.Filter
            .Eq(x => x.SleeperPlayerId, document.SleeperPlayerId);

        var update = BuildUpdate(document);

        await _collection.UpdateOneAsync(
            filter,
            update,
            new UpdateOptions { IsUpsert = true },
            CancellationToken.None);
    }

    public async Task UpsertBatchAsync(
        IEnumerable<DynastyValuationDocument> documents,
        CancellationToken ct = default)
    {
        var docList = documents.ToList();
        if (docList.Count == 0) return;

        var writes = docList.Select(document =>
        {
            var filter = Builders<DynastyValuationDocument>.Filter
                .Eq(x => x.SleeperPlayerId, document.SleeperPlayerId);

            return new UpdateOneModel<DynastyValuationDocument>(filter, BuildUpdate(document))
            {
                IsUpsert = true
            };
        }).Cast<WriteModel<DynastyValuationDocument>>().ToList();

        await _collection.BulkWriteAsync(
            writes,
            new BulkWriteOptions { IsOrdered = false },
            CancellationToken.None);
    }

    public async Task<List<DynastyValuationDocument>> GetBySleeperPlayerIdsAsync(
        IEnumerable<string> sleeperPlayerIds,
        CancellationToken ct = default)
    {
        var filter = Builders<DynastyValuationDocument>.Filter
            .In(x => x.SleeperPlayerId, sleeperPlayerIds);
        return await _collection.Find(filter).ToListAsync(ct);
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static UpdateDefinition<DynastyValuationDocument> BuildUpdate(
        DynastyValuationDocument document)
    {
        var updateDefs = new List<UpdateDefinition<DynastyValuationDocument>>
        {
            Builders<DynastyValuationDocument>.Update.Set(x => x.PlayerId, document.PlayerId),
            Builders<DynastyValuationDocument>.Update.Set(x => x.Position, document.Position),
            Builders<DynastyValuationDocument>.Update.Set(x => x.NflTeam, document.NflTeam),
            Builders<DynastyValuationDocument>.Update.Set(x => x.Age, document.Age),
            Builders<DynastyValuationDocument>.Update.Set(x => x.YearsExperience, document.YearsExperience),
            Builders<DynastyValuationDocument>.Update.Set(x => x.Season, document.Season),
            Builders<DynastyValuationDocument>.Update.Set(x => x.ScoringFormat, document.ScoringFormat),
            Builders<DynastyValuationDocument>.Update.Set(x => x.BreakoutScore, document.BreakoutScore),
            Builders<DynastyValuationDocument>.Update.Set(x => x.BreakoutClassification, document.BreakoutClassification),
            Builders<DynastyValuationDocument>.Update.Set(x => x.BreakoutSignals, document.BreakoutSignals),
            Builders<DynastyValuationDocument>.Update.Set(x => x.BreakoutScoredAt, document.BreakoutScoredAt),
            Builders<DynastyValuationDocument>.Update.Set(x => x.TradeValue, document.TradeValue),
            Builders<DynastyValuationDocument>.Update.Set(x => x.DiscountedFutureValue, document.DiscountedFutureValue),
            Builders<DynastyValuationDocument>.Update.Set(x => x.TradeValueComputedAt, document.TradeValueComputedAt),
            Builders<DynastyValuationDocument>.Update.Set(x => x.CareerValueScore, document.CareerValueScore),
            Builders<DynastyValuationDocument>.Update.Set(x => x.PeakYear, document.PeakYear),
            Builders<DynastyValuationDocument>.Update.Set(x => x.YearsOfPrimeRemaining, document.YearsOfPrimeRemaining),
            Builders<DynastyValuationDocument>.Update.Set(x => x.CareerPhase, document.CareerPhase),
            Builders<DynastyValuationDocument>.Update.SetOnInsert(x => x.Id, document.Id),
        };

        // Guard: only overwrite PlayerName if incoming value is valid
        if (!string.IsNullOrEmpty(document.PlayerName))
            updateDefs.Add(Builders<DynastyValuationDocument>.Update
                .Set(x => x.PlayerName, document.PlayerName));

        return Builders<DynastyValuationDocument>.Update.Combine(updateDefs);
    }
}
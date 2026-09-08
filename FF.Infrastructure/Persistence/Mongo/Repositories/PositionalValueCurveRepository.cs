using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class PositionalValueCurveRepository(MongoDbContext context) : IPositionalValueCurveRepository
{
    private readonly IMongoCollection<PositionalValueCurveDocument> _collection =
        context.Database.GetCollection<PositionalValueCurveDocument>("positional_value_curves");

    public async Task<List<PositionalValueCurveDocument>> GetAllBySeasonAndFormatAsync(
        int season, string scoringFormat, CancellationToken ct = default)
    {
        var filter = Builders<PositionalValueCurveDocument>.Filter.And(
            Builders<PositionalValueCurveDocument>.Filter.Eq(x => x.Season, season),
            Builders<PositionalValueCurveDocument>.Filter.Eq(x => x.ScoringFormat, scoringFormat));

        return await _collection.Find(filter).ToListAsync(ct);
    }

    public async Task<PositionalValueCurveDocument?> GetAsync(
        int season, string scoringFormat, int year, string position, CancellationToken ct = default)
        => await _collection.Find(KeyFilter(season, scoringFormat, year, position))
                            .FirstOrDefaultAsync(ct);

    public async Task UpsertBatchAsync(
        IEnumerable<PositionalValueCurveDocument> documents, CancellationToken ct = default)
    {
        var writes = new List<WriteModel<PositionalValueCurveDocument>>();

        foreach (var doc in documents)
        {
            // Id is written only on insert. Mongo rejects any update that touches
            // _id, and FAN-140 was exactly that: a bulk upsert that included the id
            // in its update document failed every write against an existing
            // collection while looking like it had succeeded.
            var update = Builders<PositionalValueCurveDocument>.Update
                .Set(x => x.DescendingSeasonValues, doc.DescendingSeasonValues)
                .Set(x => x.PoolSize, doc.PoolSize)
                .Set(x => x.Depth, doc.Depth)
                .Set(x => x.ComputedAt, doc.ComputedAt)
                .SetOnInsert(x => x.Id, doc.Id)
                .SetOnInsert(x => x.Season, doc.Season)
                .SetOnInsert(x => x.ScoringFormat, doc.ScoringFormat)
                .SetOnInsert(x => x.Year, doc.Year)
                .SetOnInsert(x => x.Position, doc.Position);

            writes.Add(new UpdateOneModel<PositionalValueCurveDocument>(
                KeyFilter(doc.Season, doc.ScoringFormat, doc.Year, doc.Position), update)
            {
                IsUpsert = true
            });
        }

        if (writes.Count == 0) return;

        await _collection.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, ct);
    }

    private static FilterDefinition<PositionalValueCurveDocument> KeyFilter(
        int season, string scoringFormat, int year, string position)
        => Builders<PositionalValueCurveDocument>.Filter.And(
            Builders<PositionalValueCurveDocument>.Filter.Eq(x => x.Season, season),
            Builders<PositionalValueCurveDocument>.Filter.Eq(x => x.ScoringFormat, scoringFormat),
            Builders<PositionalValueCurveDocument>.Filter.Eq(x => x.Year, year),
            Builders<PositionalValueCurveDocument>.Filter.Eq(x => x.Position, position));
}

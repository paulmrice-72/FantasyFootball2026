using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class AgingCurveRepository(MongoDbContext context) : IAgingCurveRepository
{
    private readonly IMongoCollection<AgingCurveDocument> _collection =
        context.Database.GetCollection<AgingCurveDocument>("aging_curves");

    public async Task<AgingCurveDocument?> GetByPositionAsync(
        string position, CancellationToken ct = default)
    {
        var filter = Builders<AgingCurveDocument>.Filter.Eq(x => x.Position, position);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<List<AgingCurveDocument>> GetAllAsync(CancellationToken ct = default)
        => await _collection.Find(Builders<AgingCurveDocument>.Filter.Empty).ToListAsync(ct);

    public async Task UpsertAsync(AgingCurveDocument document, CancellationToken ct = default)
    {
        var filter = Builders<AgingCurveDocument>.Filter.Eq(x => x.Position, document.Position);
        var update = Builders<AgingCurveDocument>.Update
            .Set(x => x.Coefficients, document.Coefficients)
            .Set(x => x.PeakAge, document.PeakAge)
            .Set(x => x.PeakValue, document.PeakValue)
            .Set(x => x.MinAge, document.MinAge)
            .Set(x => x.MaxAge, document.MaxAge)
            .Set(x => x.AgeValueMap, document.AgeValueMap)
            .Set(x => x.ComputedAt, document.ComputedAt)
            .Set(x => x.SampleSize, document.SampleSize)
            .Set(x => x.IsDefaultCurve, document.IsDefaultCurve)
            .SetOnInsert(x => x.Id, document.Id);

        await _collection.UpdateOneAsync(filter, update,
            new UpdateOptions { IsUpsert = true }, ct);
    }
}
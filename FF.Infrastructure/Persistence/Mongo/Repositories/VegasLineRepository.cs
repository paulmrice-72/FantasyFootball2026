using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class VegasLineRepository(
    MongoDbContext database,
    ILogger<VegasLineRepository> logger) : IVegasLineRepository
{
    private readonly IMongoCollection<VegasLineDocument> _collection =
        database.GetCollection<VegasLineDocument>("vegas_lines");

    public async Task UpsertAsync(VegasLineDocument document, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(document.Id))
            document.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();

        var filter = Builders<VegasLineDocument>.Filter.And(
            Builders<VegasLineDocument>.Filter.Eq(x => x.HomeTeam, document.HomeTeam),
            Builders<VegasLineDocument>.Filter.Eq(x => x.AwayTeam, document.AwayTeam),
            Builders<VegasLineDocument>.Filter.Eq(x => x.Season, document.Season),
            Builders<VegasLineDocument>.Filter.Eq(x => x.Week, document.Week));

        var existing = await _collection.Find(filter).FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            await _collection.InsertOneAsync(document, cancellationToken: ct);
        }
        else
        {
            var update = Builders<VegasLineDocument>.Update
                .Set(x => x.HomeSpread, document.HomeSpread)
                .Set(x => x.AwaySpread, document.AwaySpread)
                .Set(x => x.OverUnder, document.OverUnder)
                .Set(x => x.Bookmaker, document.Bookmaker)
                .Set(x => x.CommenceTime, document.CommenceTime)
                .Set(x => x.FetchedAt, document.FetchedAt);

            await _collection.UpdateOneAsync(
                Builders<VegasLineDocument>.Filter.Eq(x => x.Id, existing.Id),
                update, cancellationToken: ct);
        }
    }

    public async Task UpsertBatchAsync(
        IEnumerable<VegasLineDocument> documents,
        CancellationToken ct = default)
    {
        var docs = documents.ToList();
        foreach (var doc in docs)
            await UpsertAsync(doc, ct);

        logger.LogInformation("VegasLineRepository upserted {Count} documents", docs.Count);
    }

    public async Task<VegasLineDocument?> GetByTeamAsync(
        string nflTeam, int season, int week,
        CancellationToken ct = default)
    {
        // Match on either home or away team
        var filter = Builders<VegasLineDocument>.Filter.And(
            Builders<VegasLineDocument>.Filter.Eq(x => x.Season, season),
            Builders<VegasLineDocument>.Filter.Eq(x => x.Week, week),
            Builders<VegasLineDocument>.Filter.Or(
                Builders<VegasLineDocument>.Filter.Eq(x => x.HomeTeam, nflTeam),
                Builders<VegasLineDocument>.Filter.Eq(x => x.AwayTeam, nflTeam)));

        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<VegasLineDocument>> GetByWeekAsync(
        int season, int week,
        CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.Season == season && x.Week == week)
            .ToListAsync(ct);
    }
}
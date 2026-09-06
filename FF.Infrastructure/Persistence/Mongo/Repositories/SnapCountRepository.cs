// FF.Infrastructure/Persistence/Mongo/Repositories/SnapCountRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class SnapCountRepository(MongoDbContext database,
    ILogger<SnapCountRepository> logger) : ISnapCountRepository
{
    private readonly IMongoCollection<SnapCountDocument> _collection =
        database.GetCollection<SnapCountDocument>("snap_counts");

    public async Task EnsureIndexesAsync()
    {
        var indexes = new List<CreateIndexModel<SnapCountDocument>>
        {
            // Season + Week — bulk weekly queries
            new(Builders<SnapCountDocument>.IndexKeys
                .Ascending(x => x.Season)
                .Ascending(x => x.Week)),

            // Name + Team + Season + Week — merge lookup key
            // Not unique — same player can appear on multiple teams in a season
            new(Builders<SnapCountDocument>.IndexKeys
                .Ascending(x => x.PlayerName)
                .Ascending(x => x.Team)
                .Ascending(x => x.Season)
                .Ascending(x => x.Week)),
        };

        await _collection.Indexes.CreateManyAsync(indexes);

        logger.LogInformation("SnapCountRepository indexes ensured");
    }

    public async Task<(int Inserted, int Replaced)> UpsertBatchAsync(
    IEnumerable<SnapCountDocument> documents,
    CancellationToken cancellationToken = default)
    {
        var docs = documents.ToList();
        if (docs.Count == 0) return (0, 0);

        var totalInserted = 0;
        var totalReplaced = 0;
        const int batchSize = 500;

        var batches = docs
            .Select((doc, i) => new { doc, i })
            .GroupBy(x => x.i / batchSize)
            .Select(g => g.Select(x => x.doc).ToList());

        foreach (var batch in batches)
        {
            var bulkOps = batch.Select(BuildUpsert).ToList();

            var result = await _collection.BulkWriteAsync(
                bulkOps,
                new BulkWriteOptions { IsOrdered = false },
                cancellationToken);

            totalInserted += (int)result.Upserts.Count;
            totalReplaced += (int)result.ModifiedCount;
        }

        return (totalInserted, totalReplaced);
    }

    /// <summary>
    /// Upsert on the natural key (PlayerName + Team + Season + Week) WITHOUT touching _id.
    /// </summary>
    /// <remarks>
    /// This was a ReplaceOneModel(filter, doc). SnapCountDocument.Id is mapped with
    /// StringObjectIdGenerator, so every freshly parsed document serialises a brand new
    /// ObjectId. On the first import that is harmless — the upsert inserts. On every
    /// import after that the filter matches an existing row whose _id differs, and
    /// MongoDB rejects the replacement with error 66:
    ///
    ///   "After applying the update, the (immutable) field '_id' was found to have been
    ///    altered to _id: ObjectId('...')"
    ///
    /// Every row in the batch failed that way, so the collection had been frozen at its
    /// first-ever import since the feature shipped. $set of the fields with _id stripped
    /// updates existing rows in place and lets the server generate _id on insert.
    /// </remarks>
    internal static WriteModel<SnapCountDocument> BuildUpsert(SnapCountDocument doc)
    {
        var filter = Builders<SnapCountDocument>.Filter.And(
            Builders<SnapCountDocument>.Filter.Eq(x => x.PlayerName, doc.PlayerName),
            Builders<SnapCountDocument>.Filter.Eq(x => x.Team, doc.Team),
            Builders<SnapCountDocument>.Filter.Eq(x => x.Season, doc.Season),
            Builders<SnapCountDocument>.Filter.Eq(x => x.Week, doc.Week)
        );

        var fields = doc.ToBsonDocument();
        fields.Remove("_id");

        return new UpdateOneModel<SnapCountDocument>(
            filter,
            new BsonDocument("$set", fields))
        {
            IsUpsert = true
        };
    }
    public async Task<List<SnapCountDocument>> GetBySeasonWeekAsync(
        int season,
        int week,
        CancellationToken cancellationToken = default)
    {
        return await _collection
            .Find(x => x.Season == season && x.Week == week)
            .ToListAsync(cancellationToken);
    }
}

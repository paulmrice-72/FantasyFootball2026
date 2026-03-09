// FF.Infrastructure/Persistence/Mongo/Repositories/SnapCountRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
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

        var inserted = 0;
        var replaced = 0;

        foreach (var doc in docs)
        {
            var filter = Builders<SnapCountDocument>.Filter.Where(x =>
                x.PlayerName == doc.PlayerName &&
                x.Team == doc.Team &&
                x.Season == doc.Season &&
                x.Week == doc.Week);

            var result = await _collection.ReplaceOneAsync(
                filter,
                doc,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);

            if (result.MatchedCount == 0) inserted++;
            else replaced++;
        }

        return (inserted, replaced);
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

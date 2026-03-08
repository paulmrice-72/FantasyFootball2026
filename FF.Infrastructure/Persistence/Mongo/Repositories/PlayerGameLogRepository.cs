// FF.Infrastructure/Persistence/Mongo/Repositories/PlayerGameLogRepository.cs
//
// MongoDB repository for PlayerGameLogDocument.
// Handles all upsert, query, and index management operations.
//
// INDEXES (created on startup via EnsureIndexesAsync):
//   1. (PlayerId, Season, Week) — unique composite — primary upsert key
//   2. (Season, Week)           — for bulk weekly reads in projection engine
//   3. (NflTeam, Season)        — for team-level matchup analysis
//   4. (Position, Season)       — for position-group queries
//
// UPSERT STRATEGY:
//   ReplaceOneAsync with IsUpsert=true.
//   Filter: PlayerId + Season + Week (the natural unique key from nflfastR).
//   This makes the import fully idempotent — re-running the same file is safe.

using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class PlayerGameLogRepository(MongoDbContext context, ILogger<PlayerGameLogRepository> logger) : FF.Application.Interfaces.Persistence.IPlayerGameLogRepository
{
    private readonly IMongoCollection<PlayerGameLogDocument> _collection = context.Database.GetCollection<PlayerGameLogDocument>(CollectionName);
    private readonly ILogger<PlayerGameLogRepository> _logger = logger;

    public const string CollectionName = "PlayerGameLogs";

    /// <summary>
    /// Creates MongoDB indexes. Call once on startup from DI or DatabaseInitialiser.
    /// CreateMany is idempotent — safe to call on every startup.
    /// </summary>
    public async Task EnsureIndexesAsync()
    {
        // Register BSON class map if not already registered
        if (!MongoDB.Bson.Serialization.BsonClassMap.IsClassMapRegistered(typeof(PlayerGameLogDocument)))
        {
            MongoDB.Bson.Serialization.BsonClassMap.RegisterClassMap<PlayerGameLogDocument>(cm =>
            {
                cm.AutoMap();
                cm.MapIdMember(c => c.Id)
                  .SetIdGenerator(MongoDB.Bson.Serialization.IdGenerators.StringObjectIdGenerator.Instance);
            });
        }
        var indexModels = new List<CreateIndexModel<PlayerGameLogDocument>>
        {
            // Primary upsert key — unique
            new(Builders<PlayerGameLogDocument>.IndexKeys
                    .Ascending(x => x.PlayerId)
                    .Ascending(x => x.Season)
                    .Ascending(x => x.Week),
                new CreateIndexOptions { Unique = true, Name = "idx_player_season_week" }),

            // Bulk weekly reads (projection engine queries by season+week)
            new(Builders<PlayerGameLogDocument>.IndexKeys
                    .Ascending(x => x.Season)
                    .Ascending(x => x.Week),
                new CreateIndexOptions { Name = "idx_season_week" }),

            // Team-level matchup analysis
            new(Builders<PlayerGameLogDocument>.IndexKeys
                    .Ascending(x => x.NflTeam)
                    .Ascending(x => x.Season),
                new CreateIndexOptions { Name = "idx_team_season" }),

            // Position-group reads
            new(Builders<PlayerGameLogDocument>.IndexKeys
                    .Ascending(x => x.Position)
                    .Ascending(x => x.Season),
                new CreateIndexOptions { Name = "idx_position_season" }),
        };

        await _collection.Indexes.CreateManyAsync(indexModels);
        _logger.LogInformation("PlayerGameLogs MongoDB indexes ensured");
    }

    /// <summary>
    /// Upserts a batch of game log documents.
    /// Uses ReplaceOne with IsUpsert=true for idempotent imports.
    /// Returns count of documents inserted vs replaced.
    /// </summary>
    public async Task<(int Inserted, int Replaced)> UpsertBatchAsync(
        IEnumerable<PlayerGameLogDocument> documents,
        CancellationToken cancellationToken = default)
    {
        var inserted = 0;
        var replaced = 0;

        var tasks = documents.Select(async doc =>
        {
            // Ensure Id is always set — ReplaceOne upsert requires a non-null _id
            // or MongoDB will attempt to insert with _id: null, causing a duplicate key
            // error on the second document with a null Id.
            if (string.IsNullOrEmpty(doc.Id))
                doc.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();

            var filter = Builders<PlayerGameLogDocument>.Filter.And(
                Builders<PlayerGameLogDocument>.Filter.Eq(x => x.PlayerId, doc.PlayerId),
                Builders<PlayerGameLogDocument>.Filter.Eq(x => x.Season, doc.Season),
                Builders<PlayerGameLogDocument>.Filter.Eq(x => x.Week, doc.Week)
            );

            var result = await _collection.ReplaceOneAsync(
                filter,
                doc,
                new ReplaceOptions { IsUpsert = true },
                cancellationToken);

            if (result.UpsertedId != null)
                Interlocked.Increment(ref inserted);
            else
                Interlocked.Increment(ref replaced);
        });

        await Task.WhenAll(tasks);
        return (inserted, replaced);
    }

    /// <summary>
    /// Returns all game logs for a player across specified seasons.
    /// Used by the projection engine to build rolling averages.
    /// </summary>
    public async Task<List<PlayerGameLogDocument>> GetPlayerGameLogsAsync(
        string playerId,
        IEnumerable<int> seasons,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<PlayerGameLogDocument>.Filter.And(
            Builders<PlayerGameLogDocument>.Filter.Eq(x => x.PlayerId, playerId),
            Builders<PlayerGameLogDocument>.Filter.In(x => x.Season, seasons)
        );

        return await _collection
            .Find(filter)
            .SortBy(x => x.Season)
            .ThenBy(x => x.Week)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Returns all game logs for a specific season and week.
    /// Used by matchup analysis engine.
    /// </summary>
    public async Task<List<PlayerGameLogDocument>> GetWeeklyLogsAsync(
        int season,
        int week,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<PlayerGameLogDocument>.Filter.And(
            Builders<PlayerGameLogDocument>.Filter.Eq(x => x.Season, season),
            Builders<PlayerGameLogDocument>.Filter.Eq(x => x.Week, week)
        );

        return await _collection.Find(filter).ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Returns counts of documents per season — used for data quality reporting.
    /// </summary>
    public async Task<Dictionary<int, long>> GetDocumentCountsBySeasonAsync(
        CancellationToken cancellationToken = default)
    {
        var seasons = new[] { 2022, 2023, 2024 };
        var counts = new Dictionary<int, long>();

        foreach (var season in seasons)
        {
            var filter = Builders<PlayerGameLogDocument>.Filter.Eq(x => x.Season, season);
            counts[season] = await _collection.CountDocumentsAsync(filter, cancellationToken: cancellationToken);
        }

        return counts;
    }

    /// <summary>
    /// Deletes all documents for a given season. Used for full re-import.
    /// </summary>
    public async Task<long> DeleteSeasonAsync(int season, CancellationToken cancellationToken = default)
    {
        var filter = Builders<PlayerGameLogDocument>.Filter.Eq(x => x.Season, season);
        var result = await _collection.DeleteManyAsync(filter, cancellationToken);
        return result.DeletedCount;
    }
}
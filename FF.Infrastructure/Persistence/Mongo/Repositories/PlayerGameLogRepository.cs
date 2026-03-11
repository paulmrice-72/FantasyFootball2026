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

using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Diagnostics.Metrics;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class PlayerGameLogRepository(MongoDbContext context, ILogger<PlayerGameLogRepository> logger) : IPlayerGameLogRepository
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
        if (!MongoDB.Bson.Serialization.BsonClassMap.IsClassMapRegistered(
            typeof(PlayerGameLogDocument)))
        {
            MongoDB.Bson.Serialization.BsonClassMap.RegisterClassMap<PlayerGameLogDocument>(cm =>
            {
                cm.AutoMap();
                cm.MapIdMember(c => c.Id)
                  .SetIdGenerator(MongoDB.Bson.Serialization.IdGenerators.StringObjectIdGenerator.Instance)
                  .SetSerializer(new MongoDB.Bson.Serialization.Serializers.StringSerializer(
                      MongoDB.Bson.BsonType.ObjectId));  // ← ADD THIS LINE
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
    /// <summary>
    /// Upserts a batch of game log documents.
    /// Uses ReplaceOne with IsUpsert=true for idempotent imports.
    /// Returns count of documents inserted vs replaced.
    ///
    /// IMPORTANT: On insert, MongoDB generates the _id automatically.
    /// On replace, we must NOT send an _id in the replacement document
    /// because the existing document's _id is immutable.
    /// We clear doc.Id before replace so MongoDB ignores it.
    /// </summary>
    public async Task<(int Inserted, int Replaced)> UpsertBatchAsync(
        IEnumerable<PlayerGameLogDocument> documents,
        CancellationToken cancellationToken = default)
    {
        var inserted = 0;
        var replaced = 0;

        var tasks = documents.Select(async doc =>
        {
            var filter = Builders<PlayerGameLogDocument>.Filter.And(
                Builders<PlayerGameLogDocument>.Filter.Eq(x => x.PlayerId, doc.PlayerId),
                Builders<PlayerGameLogDocument>.Filter.Eq(x => x.Season, doc.Season),
                Builders<PlayerGameLogDocument>.Filter.Eq(x => x.Week, doc.Week)
            );

            // $set update — never touches _id, fully idempotent
            var update = Builders<PlayerGameLogDocument>.Update
                // Player Identity
                .Set(x => x.PlayerName, doc.PlayerName)
                .Set(x => x.DisplayName, doc.DisplayName)
                .Set(x => x.Position, doc.Position)
                .Set(x => x.NflTeam, doc.NflTeam)
                .Set(x => x.OpponentTeam, doc.OpponentTeam)
                .Set(x => x.HeadshotUrl, doc.HeadshotUrl)
                .Set(x => x.SleeperPlayerId, doc.SleeperPlayerId)
                // Game Context
                .Set(x => x.SeasonType, doc.SeasonType)
                // Passing
                .Set(x => x.Completions, doc.Completions)
                .Set(x => x.Attempts, doc.Attempts)
                .Set(x => x.PassingYards, doc.PassingYards)
                .Set(x => x.PassingTds, doc.PassingTds)
                .Set(x => x.Interceptions, doc.Interceptions)
                .Set(x => x.Sacks, doc.Sacks)
                .Set(x => x.SackYards, doc.SackYards)
                .Set(x => x.SackFumbles, doc.SackFumbles)
                .Set(x => x.SackFumblesLost, doc.SackFumblesLost)
                .Set(x => x.PassingAirYards, doc.PassingAirYards)
                .Set(x => x.PassingYardsAfterCatch, doc.PassingYardsAfterCatch)
                .Set(x => x.PassingFirstDowns, doc.PassingFirstDowns)
                .Set(x => x.PassingEpa, doc.PassingEpa)
                .Set(x => x.Passing2PtConversions, doc.Passing2PtConversions)
                .Set(x => x.Pacr, doc.Pacr)
                .Set(x => x.Dakota, doc.Dakota)
                // Rushing
                .Set(x => x.Carries, doc.Carries)
                .Set(x => x.RushingYards, doc.RushingYards)
                .Set(x => x.RushingTds, doc.RushingTds)
                .Set(x => x.RushingFumbles, doc.RushingFumbles)
                .Set(x => x.RushingFumblesLost, doc.RushingFumblesLost)
                .Set(x => x.RushingFirstDowns, doc.RushingFirstDowns)
                .Set(x => x.RushingEpa, doc.RushingEpa)
                .Set(x => x.Rushing2PtConversions, doc.Rushing2PtConversions)
                // Receiving
                .Set(x => x.Receptions, doc.Receptions)
                .Set(x => x.Targets, doc.Targets)
                .Set(x => x.ReceivingYards, doc.ReceivingYards)
                .Set(x => x.ReceivingTds, doc.ReceivingTds)
                .Set(x => x.ReceivingFumbles, doc.ReceivingFumbles)
                .Set(x => x.ReceivingFumblesLost, doc.ReceivingFumblesLost)
                .Set(x => x.ReceivingAirYards, doc.ReceivingAirYards)
                .Set(x => x.ReceivingYardsAfterCatch, doc.ReceivingYardsAfterCatch)
                .Set(x => x.ReceivingFirstDowns, doc.ReceivingFirstDowns)
                .Set(x => x.ReceivingEpa, doc.ReceivingEpa)
                .Set(x => x.Receiving2PtConversions, doc.Receiving2PtConversions)
                // Efficiency / Usage
                .Set(x => x.Racr, doc.Racr)
                .Set(x => x.TargetShare, doc.TargetShare)
                .Set(x => x.AirYardsShare, doc.AirYardsShare)
                .Set(x => x.Wopr, doc.Wopr)
                // Special Teams
                .Set(x => x.SpecialTeamsTds, doc.SpecialTeamsTds)
                // Fantasy Points
                .Set(x => x.FantasyPoints, doc.FantasyPoints)
                .Set(x => x.FantasyPointsPpr, doc.FantasyPointsPpr)
                // Data Quality
                .Set(x => x.DataSource, doc.DataSource)
                .Set(x => x.PfrValidated, doc.PfrValidated)
                .Set(x => x.PfrFantasyPoints, doc.PfrFantasyPoints)
                .Set(x => x.PfrVariance, doc.PfrVariance)
                .Set(x => x.ImportedAt, doc.ImportedAt);

            var result = await _collection.UpdateOneAsync(
                filter,
                update,
                new UpdateOptions { IsUpsert = true },
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

    public async Task<List<PlayerGameLogDocument>> GetByPlayerSeasonAsync(
    string playerId,
    int season,
    CancellationToken cancellationToken = default)
    {
        return await _collection
            .Find(x => x.PlayerId == playerId && x.Season == season)
            .SortBy(x => x.Week)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<string>> GetDistinctPlayerIdsAsync(
        int season,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<PlayerGameLogDocument>.Filter
            .Eq(x => x.Season, season);

        return await _collection
            .Distinct(x => x.PlayerId, filter, cancellationToken: cancellationToken)
            .ToListAsync(cancellationToken);
    }

    public async Task<List<PlayerGameLogDocument>> GetDocumentsWithNullSleeperIdAsync(
    CancellationToken cancellationToken = default)
    {
        var filter = Builders<PlayerGameLogDocument>.Filter.Or(
            Builders<PlayerGameLogDocument>.Filter.Exists(x => x.SleeperPlayerId, false),
            Builders<PlayerGameLogDocument>.Filter.Eq(x => x.SleeperPlayerId, null),
            Builders<PlayerGameLogDocument>.Filter.Eq(x => x.SleeperPlayerId, "")
        );

        // Only need identity fields, not all stat fields
        var projection = Builders<PlayerGameLogDocument>.Projection
            .Include(x => x.PlayerId)
            .Include(x => x.Position)
            .Include(x => x.PlayerName);

        return await _collection
            .Find(filter)
            .Project<PlayerGameLogDocument>(projection)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateSleeperPlayerIdAsync(
        string playerId,
        string sleeperPlayerId,
        CancellationToken cancellationToken = default)
    {
        // Update ALL documents for this player in one shot
        var filter = Builders<PlayerGameLogDocument>.Filter
            .Eq(x => x.PlayerId, playerId);

        var update = Builders<PlayerGameLogDocument>.Update
            .Set(x => x.SleeperPlayerId, sleeperPlayerId);

        await _collection.UpdateManyAsync(filter, update,
            cancellationToken: cancellationToken);
    }

    public async Task<List<PlayerGameLogDocument>> GetBySeasonAsync(
    int season,
    CancellationToken cancellationToken = default)
    {
        var filter = Builders<PlayerGameLogDocument>.Filter.Eq(x => x.Season, season);
        return await _collection
            .Find(filter)
            .SortBy(x => x.Week)
            .ThenBy(x => x.PlayerName)
            .ToListAsync(cancellationToken);
    }

    public async Task BulkUpdateSnapCountsAsync(
        IEnumerable<PlayerGameLogDocument> documents,
        CancellationToken cancellationToken = default)
    {
        var updates = documents.Select(doc =>
        {
            var filter = Builders<PlayerGameLogDocument>.Filter.And(
                Builders<PlayerGameLogDocument>.Filter.Eq(x => x.PlayerId, doc.PlayerId),
                Builders<PlayerGameLogDocument>.Filter.Eq(x => x.Season, doc.Season),
                Builders<PlayerGameLogDocument>.Filter.Eq(x => x.Week, doc.Week)
            );

            var update = Builders<PlayerGameLogDocument>.Update
                .Set(x => x.OffenseSnaps, doc.OffenseSnaps)
                .Set(x => x.SnapPct, doc.SnapPct);

            return new UpdateOneModel<PlayerGameLogDocument>(filter, update);
        }).ToList();

        if (updates.Count == 0) return;

        await _collection.BulkWriteAsync(updates,
            new BulkWriteOptions { IsOrdered = false },
            cancellationToken);
    }
}
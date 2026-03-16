using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class DefensiveRankingRepository(
    MongoDbContext database,
    ILogger<DefensiveRankingRepository> logger) : IDefensiveRankingRepository
{
    private readonly IMongoCollection<DefensiveRankingDocument> _collection =
        database.GetCollection<DefensiveRankingDocument>("defensive_rankings");

    public async Task UpsertAsync(
        DefensiveRankingDocument document,
        CancellationToken ct = default)
    {
        var filter = BuildFilter(document.Team, document.Position, document.Season, document.Week);

        var update = Builders<DefensiveRankingDocument>.Update
            .Set(x => x.Team, document.Team)
            .Set(x => x.Position, document.Position)
            .Set(x => x.Season, document.Season)
            .Set(x => x.Week, document.Week)
            .Set(x => x.AvgFantasyPointsAllowed, document.AvgFantasyPointsAllowed)
            .Set(x => x.AvgFantasyPointsAllowedL4W, document.AvgFantasyPointsAllowedL4W)
            .Set(x => x.SeasonPercentile, document.SeasonPercentile)
            .Set(x => x.L4WPercentile, document.L4WPercentile)
            .Set(x => x.DifficultyScore, document.DifficultyScore)
            .Set(x => x.GamesAllowed, document.GamesAllowed)
            .Set(x => x.CalculatedAt, document.CalculatedAt);

        await _collection.UpdateOneAsync(filter, update,
            new UpdateOptions { IsUpsert = true }, ct);
    }

    public async Task UpsertBatchAsync(
        IEnumerable<DefensiveRankingDocument> documents,
        CancellationToken ct = default)
    {
        var docs = documents.ToList();
        if (docs.Count == 0) return;

        const int batchSize = 500;
        var batches = docs
            .Select((doc, i) => new { doc, i })
            .GroupBy(x => x.i / batchSize)
            .Select(g => g.Select(x => x.doc).ToList());

        foreach (var batch in batches)
        {
            var bulkOps = batch.Select(doc =>
            {
                var filter = BuildFilter(doc.Team, doc.Position, doc.Season, doc.Week);

                var update = Builders<DefensiveRankingDocument>.Update
                    .Set(x => x.Team, doc.Team)
                    .Set(x => x.Position, doc.Position)
                    .Set(x => x.Season, doc.Season)
                    .Set(x => x.Week, doc.Week)
                    .Set(x => x.AvgFantasyPointsAllowed, doc.AvgFantasyPointsAllowed)
                    .Set(x => x.AvgFantasyPointsAllowedL4W, doc.AvgFantasyPointsAllowedL4W)
                    .Set(x => x.SeasonPercentile, doc.SeasonPercentile)
                    .Set(x => x.L4WPercentile, doc.L4WPercentile)
                    .Set(x => x.DifficultyScore, doc.DifficultyScore)
                    .Set(x => x.GamesAllowed, doc.GamesAllowed)
                    .Set(x => x.CalculatedAt, doc.CalculatedAt);

                return (WriteModel<DefensiveRankingDocument>)
                    new UpdateOneModel<DefensiveRankingDocument>(filter, update)
                    {
                        IsUpsert = true
                    };
            }).ToList();

            await _collection.BulkWriteAsync(
                bulkOps,
                new BulkWriteOptions { IsOrdered = false },
                ct);
        }

        logger.LogInformation(
            "DefensiveRankingRepository upserted {Count} documents", docs.Count);
    }

    public async Task<DefensiveRankingDocument?> GetAsync(
        string team, string position, int season, int week,
        CancellationToken ct = default)
    {
        return await _collection
            .Find(BuildFilter(team, position, season, week))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<DefensiveRankingDocument>> GetByWeekAsync(
        int season, int week,
        CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.Season == season && x.Week == week)
            .ToListAsync(ct);
    }

    public async Task EnsureIndexesAsync()
    {
        var indexes = new List<CreateIndexModel<DefensiveRankingDocument>>
        {
            // Primary lookup key
            new(Builders<DefensiveRankingDocument>.IndexKeys
                .Ascending(x => x.Team)
                .Ascending(x => x.Position)
                .Ascending(x => x.Season)
                .Ascending(x => x.Week),
                new CreateIndexOptions { Unique = true }),

            // Season + week bulk queries
            new(Builders<DefensiveRankingDocument>.IndexKeys
                .Ascending(x => x.Season)
                .Ascending(x => x.Week)),
        };

        await _collection.Indexes.CreateManyAsync(indexes);
        logger.LogInformation("DefensiveRankingRepository indexes ensured");
    }

private static FilterDefinition<DefensiveRankingDocument> BuildFilter(
    string team, string position, int season, int week) =>
    Builders<DefensiveRankingDocument>.Filter.And(
        Builders<DefensiveRankingDocument>.Filter.Eq(x => x.Team, team),
        Builders<DefensiveRankingDocument>.Filter.Eq(x => x.Position, position),
        Builders<DefensiveRankingDocument>.Filter.Eq(x => x.Season, season),
        Builders<DefensiveRankingDocument>.Filter.Eq(x => x.Week, week));
}


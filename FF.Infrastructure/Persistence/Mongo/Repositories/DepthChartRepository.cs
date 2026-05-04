// FF.Infrastructure/Persistence/Mongo/Repositories/DepthChartRepository.cs
using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using FF.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class DepthChartRepository(MongoDbContext db) : IDepthChartRepository
{
    private readonly IMongoCollection<DepthChartDocument> _collection =
        db.GetCollection<DepthChartDocument>("depth_charts");

    public async Task UpsertBatchAsync(
        IReadOnlyList<DepthChartDocument> rows,
        CancellationToken ct = default)
    {
        if (rows.Count == 0) return;

        var writes = rows.Select(row =>
        {
            // Natural key: season + week + gsisId + depthPosition
            var filter = Builders<DepthChartDocument>.Filter.And(
                Builders<DepthChartDocument>.Filter.Eq(d => d.Season, row.Season),
                Builders<DepthChartDocument>.Filter.Eq(d => d.Week, row.Week),
                Builders<DepthChartDocument>.Filter.Eq(d => d.GsisId, row.GsisId),
                Builders<DepthChartDocument>.Filter.Eq(d => d.DepthPosition, row.DepthPosition));

            var update = Builders<DepthChartDocument>.Update
                .Set(d => d.FullName, row.FullName)
                .Set(d => d.NflTeam, row.NflTeam)
                .Set(d => d.Position, row.Position)
                .Set(d => d.DepthTeam, row.DepthTeam)
                .Set(d => d.FormationPosition, row.FormationPosition)
                .Set(d => d.SleeperPlayerId, row.SleeperPlayerId)
                .Set(d => d.SyncedAt, row.SyncedAt);

            return new UpdateOneModel<DepthChartDocument>(filter, update)
            {
                IsUpsert = true
            };
        }).ToList();

        // Single round trip to MongoDB — replaces 111k sequential UpdateOneAsync calls
        var options = new BulkWriteOptions { IsOrdered = false }; // unordered = maximum parallelism
        await _collection.BulkWriteAsync(writes, options, ct);
    }

    public async Task<IReadOnlyList<DepthChartDocument>> GetByPlayerAsync(
        string sleeperPlayerId, int season, CancellationToken ct = default)
    {
        var filter = Builders<DepthChartDocument>.Filter.And(
            Builders<DepthChartDocument>.Filter.Eq(d => d.SleeperPlayerId, sleeperPlayerId),
            Builders<DepthChartDocument>.Filter.Eq(d => d.Season, season));

        return await _collection
            .Find(filter)
            .SortByDescending(d => d.Week)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DepthChartDocument>> GetByTeamAsync(
        string nflTeam, int season, int week, CancellationToken ct = default)
    {
        var filter = Builders<DepthChartDocument>.Filter.And(
            Builders<DepthChartDocument>.Filter.Eq(d => d.NflTeam, nflTeam),
            Builders<DepthChartDocument>.Filter.Eq(d => d.Season, season),
            Builders<DepthChartDocument>.Filter.Eq(d => d.Week, week));

        return await _collection
            .Find(filter)
            .SortBy(d => d.Position)
            .ThenBy(d => d.DepthTeam)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<DepthChartDocument>> GetLatestBySleeperIdsAsync(
        IReadOnlyList<string> sleeperPlayerIds, int season, CancellationToken ct = default)
    {
        if (sleeperPlayerIds.Count == 0) return [];

        var filter = Builders<DepthChartDocument>.Filter.And(
            Builders<DepthChartDocument>.Filter.In(d => d.SleeperPlayerId, sleeperPlayerIds),
            Builders<DepthChartDocument>.Filter.Eq(d => d.Season, season));

        var all = await _collection
            .Find(filter)
            .SortByDescending(d => d.Week)
            .ToListAsync(ct);

        // One doc per player — highest week wins
        return all
            .GroupBy(d => d.SleeperPlayerId)
            .Select(g => g.First())
            .ToList();
    }
}
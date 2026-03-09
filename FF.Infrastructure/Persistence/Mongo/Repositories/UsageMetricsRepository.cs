// FF.Infrastructure/Persistence/MongoDB/Repositories/UsageMetricsRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class UsageMetricsRepository : IUsageMetricsRepository
{
    private readonly IMongoCollection<PlayerUsageMetricsDocument> _collection;

    public UsageMetricsRepository(MongoDbContext database)
    {
        _collection = database.GetCollection<PlayerUsageMetricsDocument>(
            "player_usage_metrics");

        // Ensure unique composite index on playerId + season
        var indexKeys = Builders<PlayerUsageMetricsDocument>.IndexKeys
            .Ascending(x => x.PlayerId)
            .Ascending(x => x.Season);

        var indexOptions = new CreateIndexOptions { Unique = true };
        _collection.Indexes.CreateOne(
            new CreateIndexModel<PlayerUsageMetricsDocument>(indexKeys, indexOptions));
    }

    public async Task<PlayerUsageMetricsDocument?> GetByPlayerSeasonAsync(
        string playerId, int season, CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.PlayerId == playerId && x.Season == season)
            .FirstOrDefaultAsync(ct);
    }
    public async Task UpsertAsync(
        PlayerUsageMetricsDocument metrics,
        CancellationToken ct = default)
    {
        var filter = Builders<PlayerUsageMetricsDocument>.Filter
            .Where(x => x.PlayerId == metrics.PlayerId && x.Season == metrics.Season);

        var update = Builders<PlayerUsageMetricsDocument>.Update
            .Set(x => x.Position, metrics.Position)
            .Set(x => x.TargetShare3Wk, metrics.TargetShare3Wk)
            .Set(x => x.TargetShare5Wk, metrics.TargetShare5Wk)
            .Set(x => x.TargetShareSeason, metrics.TargetShareSeason)
            .Set(x => x.SnapPct3Wk, metrics.SnapPct3Wk)
            .Set(x => x.SnapPct5Wk, metrics.SnapPct5Wk)
            .Set(x => x.SnapPctSeason, metrics.SnapPctSeason)
            .Set(x => x.AirYardsShare3Wk, metrics.AirYardsShare3Wk)
            .Set(x => x.AirYardsShare5Wk, metrics.AirYardsShare5Wk)
            .Set(x => x.AirYardsShareSeason, metrics.AirYardsShareSeason)
            .Set(x => x.CarryShare3Wk, metrics.CarryShare3Wk)
            .Set(x => x.CarryShare5Wk, metrics.CarryShare5Wk)
            .Set(x => x.CarryShareSeason, metrics.CarryShareSeason)
            .Set(x => x.Wopr3Wk, metrics.Wopr3Wk)
            .Set(x => x.WoprSeason, metrics.WoprSeason)
            .Set(x => x.WeeksPlayed, metrics.WeeksPlayed)
            .Set(x => x.LastWeekProcessed, metrics.LastWeekProcessed)
            .Set(x => x.LastUpdated, metrics.LastUpdated);

        await _collection.UpdateOneAsync(
            filter,
            update,
            new UpdateOptions { IsUpsert = true }, ct);
    }

    public async Task<IReadOnlyList<PlayerUsageMetricsDocument>> GetBySeasonAsync(
        int season, string? position = null, CancellationToken ct = default)
    {
        var filter = Builders<PlayerUsageMetricsDocument>.Filter
            .Eq(x => x.Season, season);

        if (!string.IsNullOrEmpty(position))
            filter &= Builders<PlayerUsageMetricsDocument>.Filter
                .Eq(x => x.Position, position);

        return await _collection.Find(filter).ToListAsync(ct);
    }
}
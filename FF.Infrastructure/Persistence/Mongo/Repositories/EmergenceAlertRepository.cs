// FF.Infrastructure/Persistence/Mongo/Repositories/EmergenceAlertRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class EmergenceAlertRepository : IEmergenceAlertRepository
{
    private readonly IMongoCollection<EmergenceAlertDocument> _collection;

    public EmergenceAlertRepository(MongoDbContext database)
    {
        _collection = database.GetCollection<EmergenceAlertDocument>("emergence_alerts");

        var indexKeys = Builders<EmergenceAlertDocument>.IndexKeys
            .Ascending(x => x.PlayerId)
            .Ascending(x => x.Season)
            .Ascending(x => x.Week)
            .Ascending(x => x.TriggerSignal);

        _collection.Indexes.CreateOne(
            new CreateIndexModel<EmergenceAlertDocument>(indexKeys,
                new CreateIndexOptions { Unique = true }));
    }

    public async Task UpsertBatchAsync(
        IEnumerable<EmergenceAlertDocument> alerts,
        CancellationToken ct = default)
    {
        foreach (var alert in alerts)
        {
            var filter = Builders<EmergenceAlertDocument>.Filter.And(
                Builders<EmergenceAlertDocument>.Filter.Eq(x => x.PlayerId, alert.PlayerId),
                Builders<EmergenceAlertDocument>.Filter.Eq(x => x.Season, alert.Season),
                Builders<EmergenceAlertDocument>.Filter.Eq(x => x.Week, alert.Week),
                Builders<EmergenceAlertDocument>.Filter.Eq(x => x.TriggerSignal, alert.TriggerSignal));

            var update = Builders<EmergenceAlertDocument>.Update
                .Set(x => x.PlayerName, alert.PlayerName)
                .Set(x => x.Position, alert.Position)
                .Set(x => x.NflTeam, alert.NflTeam)
                .Set(x => x.Delta, alert.Delta)
                .Set(x => x.DetectedAt, alert.DetectedAt)
                .SetOnInsert(x => x.IsAcknowledged, false);

            await _collection.UpdateOneAsync(
                filter, update, new UpdateOptions { IsUpsert = true }, CancellationToken.None);
        }
    }

    public async Task<IReadOnlyList<EmergenceAlertDocument>> GetBySeasonWeekAsync(
        int season, int week, string? position = null, CancellationToken ct = default)
    {
        var filter = Builders<EmergenceAlertDocument>.Filter.And(
            Builders<EmergenceAlertDocument>.Filter.Eq(x => x.Season, season),
            Builders<EmergenceAlertDocument>.Filter.Eq(x => x.Week, week));

        if (!string.IsNullOrEmpty(position))
            filter &= Builders<EmergenceAlertDocument>.Filter.Eq(x => x.Position, position);

        return await _collection.Find(filter)
            .SortByDescending(x => x.Delta)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<EmergenceAlertDocument>> GetLatestBySeasonAsync(
        int season, string? position = null, CancellationToken ct = default)
    {
        var filter = Builders<EmergenceAlertDocument>.Filter.Eq(x => x.Season, season);

        if (!string.IsNullOrEmpty(position))
            filter &= Builders<EmergenceAlertDocument>.Filter.Eq(x => x.Position, position);

        var latestAlert = await _collection.Find(filter)
            .SortByDescending(x => x.Week)
            .FirstOrDefaultAsync(ct);

        if (latestAlert is null)
            return [];

        return await GetBySeasonWeekAsync(season, latestAlert.Week, position, ct);
    }
}
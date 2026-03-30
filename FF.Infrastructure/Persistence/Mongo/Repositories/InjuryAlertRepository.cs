using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class InjuryAlertRepository(MongoDbContext context) : IInjuryAlertRepository
{
    private readonly IMongoCollection<InjuryAlertDocument> _collection =
        context.GetCollection<InjuryAlertDocument>("injury_alerts");

    public async Task UpsertBatchAsync(
        IEnumerable<InjuryAlertDocument> alerts, CancellationToken ct = default)
    {
        foreach (var alert in alerts)
        {
            var filter = Builders<InjuryAlertDocument>.Filter
                .Eq(x => x.SleeperPlayerId, alert.SleeperPlayerId);

            var update = Builders<InjuryAlertDocument>.Update
                .Set(x => x.PlayerName, alert.PlayerName)
                .Set(x => x.Position, alert.Position)
                .Set(x => x.NflTeam, alert.NflTeam)
                .Set(x => x.Designation, alert.Designation)
                .Set(x => x.SyncedAt, alert.SyncedAt)
                .SetOnInsert(x => x.Id, alert.SleeperPlayerId);

            await _collection.UpdateOneAsync(
                filter, update,
                new UpdateOptions { IsUpsert = true },
                ct);
        }
    }

    public async Task<IReadOnlyList<InjuryAlertDocument>> GetActiveAlertsAsync(
        string? position = null, CancellationToken ct = default)
    {
        var filter = position is null
            ? Builders<InjuryAlertDocument>.Filter.Empty
            : Builders<InjuryAlertDocument>.Filter.Eq(x => x.Position, position);

        return await _collection
            .Find(filter)
            .SortBy(x => x.Position)
            .ThenBy(x => x.PlayerName)
            .ToListAsync(ct);
    }

    public async Task DeleteAllAsync(CancellationToken ct = default)
    {
        await _collection.DeleteManyAsync(
            Builders<InjuryAlertDocument>.Filter.Empty, ct);
    }
}
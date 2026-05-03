using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class RosterPlayerRepository(
    MongoDbContext database,
    ILogger<RosterPlayerRepository> logger)
    : IRosterPlayerRepository
{
    private readonly IMongoCollection<RosterPlayerDocument> _collection =
        database.GetCollection<RosterPlayerDocument>("roster_players");

    public async Task UpsertAsync(RosterPlayerDocument document, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(document.Id))
            document.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();

        var filter = Builders<RosterPlayerDocument>.Filter.And(
            Builders<RosterPlayerDocument>.Filter.Eq(x => x.SleeperRosterId, document.SleeperRosterId),
            Builders<RosterPlayerDocument>.Filter.Eq(x => x.SleeperLeagueId, document.SleeperLeagueId));

        var existing = await _collection.Find(filter).FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            await _collection.InsertOneAsync(document, cancellationToken: ct);
        }
        else
        {
            var update = Builders<RosterPlayerDocument>.Update
                .Set(x => x.OwnerName, document.OwnerName)
                .Set(x => x.TeamName, document.TeamName)
                .Set(x => x.SleeperUserId, document.SleeperUserId)
                .Set(x => x.OwnerAvatar, document.OwnerAvatar)
                .Set(x => x.PlayerIds, document.PlayerIds)
                .Set(x => x.StarterIds, document.StarterIds)
                .Set(x => x.IrIds, document.IrIds)
                .Set(x => x.TaxiIds, document.TaxiIds)
                .Set(x => x.OwnedPicks, document.OwnedPicks)   // ← ADDED
                .Set(x => x.Season, document.Season)
                .Set(x => x.Wins, document.Wins)
                .Set(x => x.Losses, document.Losses)
                .Set(x => x.Ties, document.Ties)
                .Set(x => x.WaiverPosition, document.WaiverPosition)
                .Set(x => x.SyncedAt, document.SyncedAt);

            await _collection.UpdateOneAsync(
                Builders<RosterPlayerDocument>.Filter.Eq(x => x.Id, existing.Id),
                update,
                cancellationToken: CancellationToken.None);
        }
    }

    public async Task UpsertBatchAsync(
        IEnumerable<RosterPlayerDocument> documents,
        CancellationToken ct = default)
    {
        var docs = documents.ToList();
        foreach (var doc in docs)
            await UpsertAsync(doc, ct);

        logger.LogInformation(
            "RosterPlayerRepository upserted {Count} documents", docs.Count);
    }

    public async Task<RosterPlayerDocument?> GetByRosterIdAsync(
        string sleeperRosterId,
        string sleeperLeagueId,
        CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.SleeperRosterId == sleeperRosterId &&
                       x.SleeperLeagueId == sleeperLeagueId)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<RosterPlayerDocument>> GetByLeagueAsync(
        string sleeperLeagueId,
        CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.SleeperLeagueId == sleeperLeagueId)
            .ToListAsync(ct);
    }

    public async Task<RosterPlayerDocument?> GetByPlayerIdAsync(
        string sleeperPlayerId,
        string sleeperLeagueId,
        CancellationToken ct = default)
    {
        var filter = Builders<RosterPlayerDocument>.Filter.And(
            Builders<RosterPlayerDocument>.Filter.Eq(x => x.SleeperLeagueId, sleeperLeagueId),
            Builders<RosterPlayerDocument>.Filter.AnyEq(x => x.PlayerIds, sleeperPlayerId));

        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<RosterPlayerDocument?> GetBySleeperUserIdAsync(
        string sleeperUserId,
        string sleeperLeagueId,
        CancellationToken ct = default) =>
        await _collection
            .Find(x => x.SleeperUserId == sleeperUserId &&
                       x.SleeperLeagueId == sleeperLeagueId)
            .FirstOrDefaultAsync(ct);
}
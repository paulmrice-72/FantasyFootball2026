// FF.Infrastructure/Persistence/MongoDB/Repositories/FantasyProsRookieRankingRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class FantasyProsRookieRankingRepository(MongoDbContext context)
    : IFantasyProsRookieRankingRepository
{
    private readonly IMongoCollection<FantasyProsRookieRankingDocument> _collection =
        context.GetCollection<FantasyProsRookieRankingDocument>("fantasyPros_rookie_rankings");

    public async Task<IReadOnlyList<FantasyProsRookieRankingDocument>> GetBySleeperPlayerIdsAsync(
        IEnumerable<string> sleeperPlayerIds,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<FantasyProsRookieRankingDocument>.Filter
            .In(x => x.SleeperPlayerId, sleeperPlayerIds);

        return await _collection
            .Find(filter)
            .ToListAsync(cancellationToken);
    }

    public async Task UpsertAsync(
        FantasyProsRookieRankingDocument document,
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<FantasyProsRookieRankingDocument>.Filter
            .Eq(x => x.SleeperPlayerId, document.SleeperPlayerId);

        var update = Builders<FantasyProsRookieRankingDocument>.Update
            .Set(x => x.PlayerName, document.PlayerName)
            .Set(x => x.Position, document.Position)
            .Set(x => x.NflTeam, document.NflTeam)
            .Set(x => x.FantasyProsRank, document.FantasyProsRank)
            .Set(x => x.PositionRank, document.PositionRank)
            .Set(x => x.Tier, document.Tier)
            .Set(x => x.Season, document.Season)
            .Set(x => x.ImportedAt, document.ImportedAt);

        await _collection.UpdateOneAsync(
            filter, update,
            new UpdateOptions { IsUpsert = true },
            cancellationToken);
    }

    public async Task UpsertManyAsync(
        IEnumerable<FantasyProsRookieRankingDocument> documents,
        CancellationToken cancellationToken = default)
    {
        foreach (var doc in documents)
            await UpsertAsync(doc, cancellationToken);
    }
}
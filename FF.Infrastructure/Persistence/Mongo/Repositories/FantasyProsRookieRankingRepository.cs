// FF.Infrastructure/Persistence/Mongo/Repositories/FantasyProsRookieRankingRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

// DRAFT-PARITY-001 (2026-05-07):
// All read methods now filter on RankingType = "Rookie" so the Dynasty
// rankings stored in the same collection don't bleed into rookie joins.
// Without this filter the rookie pool join was returning ~522 dynasty
// rows alongside the ~126 actual rookies — corrupting the FantasyProsRank
// values for every player joined.
public class FantasyProsRookieRankingRepository(MongoDbContext context) : IFantasyProsRookieRankingRepository
{
    private const string RookieType = "Rookie";

    private readonly IMongoCollection<FantasyProsRookieRankingDocument> _collection =
        context.GetCollection<FantasyProsRookieRankingDocument>("fantasyPros_rookie_rankings");

    public async Task<IReadOnlyList<FantasyProsRookieRankingDocument>> GetBySleeperPlayerIdsAsync(
        IEnumerable<string> sleeperPlayerIds, CancellationToken cancellationToken = default)
    {
        var filter = Builders<FantasyProsRookieRankingDocument>.Filter.And(
            Builders<FantasyProsRookieRankingDocument>.Filter.In(x => x.SleeperPlayerId, sleeperPlayerIds),
            Builders<FantasyProsRookieRankingDocument>.Filter.Eq(x => x.RankingType, RookieType));

        return await _collection.Find(filter).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FantasyProsRookieRankingDocument>> GetAllBySeasonAsync(
        int season, CancellationToken cancellationToken = default)
    {
        // NOTE: Returns ALL ranking types for the season. Dynasty consumers depend on this.
        // For rookie-specific reads, use GetAllBySeasonAndTypeAsync(season, "Rookie") or
        // GetBySleeperPlayerIdsAsync (which now self-filters to Rookie type).
        var filter = Builders<FantasyProsRookieRankingDocument>.Filter
            .Eq(x => x.Season, season);

        return await _collection.Find(filter).SortBy(x => x.FantasyProsRank).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FantasyProsRookieRankingDocument>> GetAllBySeasonAndTypeAsync(
        int season, string rankingType, CancellationToken cancellationToken = default)
    {
        // Caller-controlled type — used by services that legitimately need Dynasty rankings.
        var filter = Builders<FantasyProsRookieRankingDocument>.Filter.And(
            Builders<FantasyProsRookieRankingDocument>.Filter.Eq(x => x.Season, season),
            Builders<FantasyProsRookieRankingDocument>.Filter.Eq(x => x.RankingType, rankingType));

        return await _collection.Find(filter).SortBy(x => x.FantasyProsRank).ToListAsync(cancellationToken);
    }

    public async Task UpsertAsync(
        FantasyProsRookieRankingDocument document, CancellationToken cancellationToken = default)
    {
        // Key on Id (which includes season + rankingType suffix) — prevents cross-season
        // duplicate key collisions that occur when filtering on SleeperPlayerId + RankingType alone.
        var filter = Builders<FantasyProsRookieRankingDocument>.Filter
            .Eq(d => d.Id, document.Id);

        var update = Builders<FantasyProsRookieRankingDocument>.Update
            .SetOnInsert(x => x.Id, document.Id)
            .Set(x => x.SleeperPlayerId, document.SleeperPlayerId)
            .Set(x => x.PlayerName, document.PlayerName)
            .Set(x => x.Position, document.Position)
            .Set(x => x.NflTeam, document.NflTeam)
            .Set(x => x.FantasyProsRank, document.FantasyProsRank)
            .Set(x => x.PositionRank, document.PositionRank)
            .Set(x => x.Tier, document.Tier)
            .Set(x => x.Season, document.Season)
            .Set(x => x.RankingType, document.RankingType)
            .Set(x => x.ImportedAt, document.ImportedAt);

        await _collection.UpdateOneAsync(
            filter, update, new UpdateOptions { IsUpsert = true }, CancellationToken.None);
    }

    public async Task UpsertManyAsync(
        IEnumerable<FantasyProsRookieRankingDocument> documents, CancellationToken cancellationToken = default)
    {
        foreach (var doc in documents)
            await UpsertAsync(doc, cancellationToken);
    }
}
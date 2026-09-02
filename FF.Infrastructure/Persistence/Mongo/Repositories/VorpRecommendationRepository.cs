// FF.Infrastructure/Persistence/Mongo/Repositories/VorpRecommendationRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class VorpRecommendationRepository : IVorpRecommendationRepository
{
    private readonly IMongoCollection<VorpRecommendationDocument> _collection;

    // The pre-FAN-118 unique index, keyed without a league. Named here because it
    // has to be dropped, not merely superseded — see the constructor.
    private const string LegacyUniqueIndexName = "PlayerId_1_Season_1_Week_1";

    public VorpRecommendationRepository(MongoDbContext database)
    {
        _collection = database.GetCollection<VorpRecommendationDocument>("vorp_recommendations");

        // FAN-118 — the old unique index was on (PlayerId, Season, Week). Creating the
        // new one does NOT replace it: both would exist, and the old one would then
        // reject the second league's row for any player, since two leagues legitimately
        // produce two rows with the same player/season/week. It has to go.
        //
        // Safe to drop unconditionally: the collection is empty in every environment
        // (nothing has ever written VORP), so there is no index being relied on here.
        try
        {
            _collection.Indexes.DropOne(LegacyUniqueIndexName);
        }
        catch (MongoCommandException)
        {
            // "index not found" — already dropped, or a fresh database. Either is fine.
        }

        var indexKeys = Builders<VorpRecommendationDocument>.IndexKeys
            .Ascending(x => x.SleeperLeagueId)
            .Ascending(x => x.Season)
            .Ascending(x => x.Week)
            .Ascending(x => x.PlayerId);

        _collection.Indexes.CreateOne(
            new CreateIndexModel<VorpRecommendationDocument>(indexKeys,
                new CreateIndexOptions { Unique = true, Name = "league_season_week_player" }));
    }

    public async Task UpsertBatchAsync(
        IEnumerable<VorpRecommendationDocument> recommendations,
        CancellationToken ct = default)
    {
        var models = new List<WriteModel<VorpRecommendationDocument>>();

        foreach (var rec in recommendations)
        {
            var filter = Builders<VorpRecommendationDocument>.Filter.And(
                Builders<VorpRecommendationDocument>.Filter.Eq(x => x.SleeperLeagueId, rec.SleeperLeagueId),
                Builders<VorpRecommendationDocument>.Filter.Eq(x => x.Season, rec.Season),
                Builders<VorpRecommendationDocument>.Filter.Eq(x => x.Week, rec.Week),
                Builders<VorpRecommendationDocument>.Filter.Eq(x => x.PlayerId, rec.PlayerId));

            // Explicit .Set list rather than ReplaceOne — replacing would also rewrite
            // _id and any field added later but not yet mapped here.
            var update = Builders<VorpRecommendationDocument>.Update
                .Set(x => x.PlayerName, rec.PlayerName)
                .Set(x => x.Position, rec.Position)
                .Set(x => x.NflTeam, rec.NflTeam)
                .Set(x => x.IsRostered, rec.IsRostered)
                .Set(x => x.ProjectedPoints, rec.ProjectedPoints)
                .Set(x => x.FloorPoints, rec.FloorPoints)
                .Set(x => x.CeilingPoints, rec.CeilingPoints)
                .Set(x => x.ReplacementLevel, rec.ReplacementLevel)
                .Set(x => x.Vorp, rec.Vorp)
                .Set(x => x.ReplacementLevelFreeAgent, rec.ReplacementLevelFreeAgent)
                .Set(x => x.VorpFreeAgent, rec.VorpFreeAgent)
                .Set(x => x.ReplacementPoolExhausted, rec.ReplacementPoolExhausted)
                .Set(x => x.VorpRank, rec.VorpRank)
                .Set(x => x.PositionRank, rec.PositionRank)
                .Set(x => x.ComputedAt, rec.ComputedAt);

            models.Add(new UpdateOneModel<VorpRecommendationDocument>(filter, update) { IsUpsert = true });
        }

        if (models.Count == 0) return;

        await _collection.BulkWriteAsync(models, new BulkWriteOptions { IsOrdered = false }, ct);
    }

    /// <summary>
    /// Filtered and sorted in memory, deliberately — do NOT move <c>Vorp</c> back
    /// into the server-side <c>Find</c>.
    ///
    /// Until FAN-129's migration has run in every environment, the driver persists
    /// <c>decimal</c> as a BSON string, and every string sorts above every number in
    /// BSON order. <c>SortByDescending(x =&gt; x.Vorp)</c> was therefore lexicographic:
    /// "9.4" ranked above "18.7". Combined with the <c>Limit(top)</c> that followed
    /// it, that did not merely misorder the result — it selected the wrong players,
    /// cutting the genuinely highest-VORP names out before they were returned.
    /// </summary>
    public async Task<IReadOnlyList<VorpRecommendationDocument>> GetByWeekAsync(
        string sleeperLeagueId,
        int season,
        int week,
        string? position = null,
        int top = 50,
        CancellationToken ct = default)
    {
        var filter = Builders<VorpRecommendationDocument>.Filter.And(
            Builders<VorpRecommendationDocument>.Filter.Eq(x => x.SleeperLeagueId, sleeperLeagueId),
            Builders<VorpRecommendationDocument>.Filter.Eq(x => x.Season, season),
            Builders<VorpRecommendationDocument>.Filter.Eq(x => x.Week, week));

        if (!string.IsNullOrEmpty(position))
            filter &= Builders<VorpRecommendationDocument>.Filter
                .Eq(x => x.Position, position);

        var docs = await _collection.Find(filter).ToListAsync(ct);

        return docs
            .OrderByDescending(d => d.Vorp)
            .Take(top)
            .ToList();
    }

    public async Task DeleteForWeekAsync(
        string sleeperLeagueId,
        int season,
        int week,
        CancellationToken ct = default)
    {
        var filter = Builders<VorpRecommendationDocument>.Filter.And(
            Builders<VorpRecommendationDocument>.Filter.Eq(x => x.SleeperLeagueId, sleeperLeagueId),
            Builders<VorpRecommendationDocument>.Filter.Eq(x => x.Season, season),
            Builders<VorpRecommendationDocument>.Filter.Eq(x => x.Week, week));

        await _collection.DeleteManyAsync(filter, ct);
    }
}

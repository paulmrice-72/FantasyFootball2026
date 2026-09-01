// FF.Infrastructure/Persistence/Mongo/Repositories/PlayerProjectionRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class PlayerProjectionRepository(
    MongoDbContext database,
    ILogger<PlayerProjectionRepository> logger) : IPlayerProjectionRepository
{
    private readonly IMongoCollection<PlayerProjectionDocument> _collection =
        database.GetCollection<PlayerProjectionDocument>("player_projections");

    private readonly ILogger<PlayerProjectionRepository> _logger = logger;

    public async Task UpsertAsync(PlayerProjectionDocument doc, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(doc.Id))
            doc.Id = ObjectId.GenerateNewId().ToString();

        var filter = Builders<PlayerProjectionDocument>.Filter.And(
            Builders<PlayerProjectionDocument>.Filter.Eq(x => x.PlayerId, doc.PlayerId),
            Builders<PlayerProjectionDocument>.Filter.Eq(x => x.Season, doc.Season),
            Builders<PlayerProjectionDocument>.Filter.Eq(x => x.Week, doc.Week));

        var existing = await _collection.Find(filter).FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            await _collection.InsertOneAsync(doc, cancellationToken: ct);
            return;
        }

        // NOTE: this is an explicit field list, not a whole-document replace, because
        // ReplaceOneAsync is banned in this codebase. Every new field on
        // PlayerProjectionDocument must be added here or it silently never persists
        // on the second and subsequent runs.
        var update = Builders<PlayerProjectionDocument>.Update
            .Set(x => x.PlayerName, doc.PlayerName)
            .Set(x => x.SleeperPlayerId, doc.SleeperPlayerId)
            .Set(x => x.Position, doc.Position)
            .Set(x => x.NflTeam, doc.NflTeam)
            .Set(x => x.OpponentTeam, doc.OpponentTeam)

            // ── L0 canonical output ───────────────────────────────────────
            .Set(x => x.StatLine, doc.StatLine)
            .Set(x => x.Basis, doc.Basis)
            .Set(x => x.BasisSeason, doc.BasisSeason)

            // ── L1 cached point values ────────────────────────────────────
            .Set(x => x.ProjectedPoints, doc.ProjectedPoints)
            .Set(x => x.ProjectedPointsPpr, doc.ProjectedPointsPpr)
            .Set(x => x.ProjectedPointsHalfPpr, doc.ProjectedPointsHalfPpr)

            // ── Model inputs / transparency ───────────────────────────────
            .Set(x => x.WeightedAvgPoints, doc.WeightedAvgPoints)
            .Set(x => x.MatchupAdjustmentFactor, doc.MatchupAdjustmentFactor)
            .Set(x => x.SnapPctInput, doc.SnapPctInput)
            .Set(x => x.TargetShareInput, doc.TargetShareInput)
            .Set(x => x.UsageTrendMultiplier, doc.UsageTrendMultiplier)
            .Set(x => x.AvailabilityRate, doc.AvailabilityRate)
            .Set(x => x.GameSampleSize, doc.GameSampleSize)
            .Set(x => x.RSquared, doc.RSquared)
            .Set(x => x.ScoringFormat, doc.ScoringFormat)

            // ── Game script context ───────────────────────────────────────
            .Set(x => x.GameScript, doc.GameScript)
            .Set(x => x.RbVolumeMultiplier, doc.RbVolumeMultiplier)
            .Set(x => x.WrTeVolumeMultiplier, doc.WrTeVolumeMultiplier)
            .Set(x => x.SpreadInput, doc.SpreadInput)

            .Set(x => x.CalculatedAt, doc.CalculatedAt);

        await _collection.UpdateOneAsync(
            Builders<PlayerProjectionDocument>.Filter.Eq(x => x.Id, existing.Id),
            update,
            cancellationToken: ct);
    }

    public async Task UpsertBatchAsync(IEnumerable<PlayerProjectionDocument> projections, CancellationToken ct = default)
    {
        foreach (var proj in projections)
            await UpsertAsync(proj, ct);
    }

    public async Task<IReadOnlyList<PlayerProjectionDocument>> GetByWeekAsync(int season, int week, CancellationToken ct = default)
    {
        var filter = Builders<PlayerProjectionDocument>.Filter.And(
            Builders<PlayerProjectionDocument>.Filter.Eq(x => x.Season, season),
            Builders<PlayerProjectionDocument>.Filter.Eq(x => x.Week, week));
        return await _collection.Find(filter).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PlayerProjectionDocument>> GetByPositionAsync(int season, int week, string position, CancellationToken ct = default)
    {
        var filter = Builders<PlayerProjectionDocument>.Filter.And(
            Builders<PlayerProjectionDocument>.Filter.Eq(x => x.Season, season),
            Builders<PlayerProjectionDocument>.Filter.Eq(x => x.Week, week),
            Builders<PlayerProjectionDocument>.Filter.Eq(x => x.Position, position));
        return await _collection.Find(filter).SortByDescending(x => x.ProjectedPointsHalfPpr).ToListAsync(ct);
    }

    public async Task<PlayerProjectionDocument?> GetByPlayerAsync(string playerId, int season, int week, CancellationToken ct = default)
    {
        var filter = Builders<PlayerProjectionDocument>.Filter.And(
            Builders<PlayerProjectionDocument>.Filter.Eq(x => x.PlayerId, playerId),
            Builders<PlayerProjectionDocument>.Filter.Eq(x => x.Season, season),
            Builders<PlayerProjectionDocument>.Filter.Eq(x => x.Week, week));
        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<PlayerProjectionDocument>> GetBySleeperIdsAsync(
        IEnumerable<string> sleeperIds, int season, int week, CancellationToken ct = default)
    {
        var ids = sleeperIds.ToList();
        if (ids.Count == 0) return [];

        var filter = Builders<PlayerProjectionDocument>.Filter.And(
            Builders<PlayerProjectionDocument>.Filter.In(x => x.SleeperPlayerId, ids),
            Builders<PlayerProjectionDocument>.Filter.Eq(x => x.Season, season),
            Builders<PlayerProjectionDocument>.Filter.Eq(x => x.Week, week));

        return await _collection.Find(filter).ToListAsync(ct);
    }
}

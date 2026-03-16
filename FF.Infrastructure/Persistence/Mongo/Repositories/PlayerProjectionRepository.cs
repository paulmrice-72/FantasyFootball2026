// FF.Infrastructure/Repositories/PlayerProjectionRepository.cs
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
        var filter = Builders<PlayerProjectionDocument>.Filter.And(
            Builders<PlayerProjectionDocument>.Filter.Eq(x => x.PlayerId, doc.PlayerId),
            Builders<PlayerProjectionDocument>.Filter.Eq(x => x.Season, doc.Season),
            Builders<PlayerProjectionDocument>.Filter.Eq(x => x.Week, doc.Week));

        var update = Builders<PlayerProjectionDocument>.Update
            .Set(x => x.PlayerName, doc.PlayerName)
            .Set(x => x.Position, doc.Position)
            .Set(x => x.NflTeam, doc.NflTeam)
            .Set(x => x.OpponentTeam, doc.OpponentTeam)
            .Set(x => x.SleeperPlayerId, doc.SleeperPlayerId)
            .Set(x => x.ProjectedPoints, doc.ProjectedPoints)
            .Set(x => x.ProjectedPointsPpr, doc.ProjectedPointsPpr)
            .Set(x => x.ProjectedPointsHalfPpr, doc.ProjectedPointsHalfPpr)
            .Set(x => x.WeightedAvgPoints, doc.WeightedAvgPoints)
            .Set(x => x.MatchupAdjustmentFactor, doc.MatchupAdjustmentFactor)
            .Set(x => x.SnapPctInput, doc.SnapPctInput)
            .Set(x => x.TargetShareInput, doc.TargetShareInput)
            .Set(x => x.GameSampleSize, doc.GameSampleSize)
            .Set(x => x.RSquared, doc.RSquared)
            .Set(x => x.CalculatedAt, doc.CalculatedAt)
            .SetOnInsert(x => x.Id, ObjectId.GenerateNewId().ToString());

        await _collection.UpdateOneAsync(filter, update,
            new UpdateOptions { IsUpsert = true }, ct);
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
}
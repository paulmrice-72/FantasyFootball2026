// FF.Infrastructure/Persistence/Mongo/Repositories/SimulationResultRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class SimulationResultRepository(
    MongoDbContext database,
    ILogger<SimulationResultRepository> logger) : ISimulationResultRepository
{
    private readonly IMongoCollection<SimulationResultDocument> _collection =
        database.GetCollection<SimulationResultDocument>("simulation_results");

    public async Task UpsertAsync(
        SimulationResultDocument document,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(document.Id))
            document.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();

        var filter = Builders<SimulationResultDocument>.Filter.And(
            Builders<SimulationResultDocument>.Filter.Eq(x => x.PlayerId, document.PlayerId),
            Builders<SimulationResultDocument>.Filter.Eq(x => x.Season, document.Season),
            Builders<SimulationResultDocument>.Filter.Eq(x => x.Week, document.Week));

        var existing = await _collection.Find(filter).FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            await _collection.InsertOneAsync(document, cancellationToken: ct);
        }
        else
        {
            var update = Builders<SimulationResultDocument>.Update
                .Set(x => x.PlayerName, document.PlayerName)
                .Set(x => x.Position, document.Position)
                .Set(x => x.NflTeam, document.NflTeam)
                .Set(x => x.OpponentTeam, document.OpponentTeam)
                .Set(x => x.Iterations, document.Iterations)
                .Set(x => x.BaseProjection, document.BaseProjection)
                .Set(x => x.StandardDeviation, document.StandardDeviation)
                .Set(x => x.Floor, document.Floor)
                .Set(x => x.Median, document.Median)
                .Set(x => x.Ceiling, document.Ceiling)
                .Set(x => x.Mean, document.Mean)
                .Set(x => x.BoomProbability, document.BoomProbability)
                .Set(x => x.BustProbability, document.BustProbability)
                .Set(x => x.PlayerRole, document.PlayerRole)
                .Set(x => x.ScoringFormat, document.ScoringFormat)
                .Set(x => x.CalculatedAt, document.CalculatedAt);

            await _collection.UpdateOneAsync(
                Builders<SimulationResultDocument>.Filter.Eq(x => x.Id, existing.Id),
                update,
                cancellationToken: ct);
        }
    }

    public async Task UpsertBatchAsync(
        IEnumerable<SimulationResultDocument> documents,
        CancellationToken ct = default)
    {
        var docs = documents.ToList();
        foreach (var doc in docs)
            await UpsertAsync(doc, ct);

        logger.LogInformation(
            "SimulationResultRepository upserted {Count} documents", docs.Count);
    }

    public async Task<SimulationResultDocument?> GetByPlayerAsync(
        string playerId, int season, int week,
        CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.PlayerId == playerId && x.Season == season && x.Week == week)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<SimulationResultDocument>> GetByWeekAsync(
        int season, int week,
        CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.Season == season && x.Week == week)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SimulationResultDocument>> GetByPositionAsync(
        int season, int week, string position,
        CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.Season == season && x.Week == week && x.Position == position)
            .SortByDescending(x => x.Median)
            .ToListAsync(ct);
    }
}
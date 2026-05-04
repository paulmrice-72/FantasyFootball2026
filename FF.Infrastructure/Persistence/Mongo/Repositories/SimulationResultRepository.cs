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
                .Set(x => x.SleeperPlayerId, document.SleeperPlayerId)
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
                .Set(x => x.CalculatedAt, document.CalculatedAt)
                .Set(x => x.Spread, document.Spread)
                .Set(x => x.GameScript, document.GameScript);

            await _collection.UpdateOneAsync(
                Builders<SimulationResultDocument>.Filter.Eq(x => x.Id, existing.Id),
                update,
                cancellationToken: CancellationToken.None);
        }
    }

    public async Task UpsertBatchAsync(
        IEnumerable<SimulationResultDocument> documents,
        CancellationToken ct = default)
    {
        var docs = documents.ToList();

        // Season-average docs (Week=0) are keyed by SleeperPlayerId — they have no
        // PlayerId (nflverse GSIS id). Use a dedicated bulk path to avoid the
        // PlayerId=null collision in the standard UpsertAsync filter.
        var seasonAvgDocs = docs.Where(d => d.Week == 0).ToList();
        var weeklyDocs = docs.Where(d => d.Week != 0).ToList();

        if (seasonAvgDocs.Count > 0)
            await UpsertSeasonAverageBatchAsync(seasonAvgDocs, ct);

        foreach (var doc in weeklyDocs)
            await UpsertAsync(doc, ct);

        logger.LogInformation(
            "SimulationResultRepository upserted {Count} documents ({SeasonAvg} season-avg, {Weekly} weekly)",
            docs.Count, seasonAvgDocs.Count, weeklyDocs.Count);
    }

    private async Task UpsertSeasonAverageBatchAsync(
        List<SimulationResultDocument> docs, CancellationToken ct)
    {
        var bulkOps = new List<WriteModel<SimulationResultDocument>>();

        foreach (var doc in docs)
        {
            if (string.IsNullOrEmpty(doc.Id))
                doc.Id = MongoDB.Bson.ObjectId.GenerateNewId().ToString();

            // Key: SleeperPlayerId + Season + Week=0
            var filter = Builders<SimulationResultDocument>.Filter.And(
                Builders<SimulationResultDocument>.Filter.Eq(x => x.SleeperPlayerId, doc.SleeperPlayerId),
                Builders<SimulationResultDocument>.Filter.Eq(x => x.Season, doc.Season),
                Builders<SimulationResultDocument>.Filter.Eq(x => x.Week, 0));

            var update = Builders<SimulationResultDocument>.Update
                .SetOnInsert(x => x.Id, doc.Id)
                .Set(x => x.PlayerName, doc.PlayerName)
                .Set(x => x.Position, doc.Position)
                .Set(x => x.NflTeam, doc.NflTeam)
                .Set(x => x.Median, doc.Median)
                .Set(x => x.Floor, doc.Floor)
                .Set(x => x.Ceiling, doc.Ceiling)
                .Set(x => x.Mean, doc.Mean)
                .Set(x => x.BaseProjection, doc.BaseProjection)
                .Set(x => x.StandardDeviation, doc.StandardDeviation)
                .Set(x => x.BoomProbability, doc.BoomProbability)
                .Set(x => x.BustProbability, doc.BustProbability)
                .Set(x => x.ScoringFormat, doc.ScoringFormat)
                .Set(x => x.PlayerRole, doc.PlayerRole)
                .Set(x => x.CalculatedAt, doc.CalculatedAt)
                .Set(x => x.Iterations, doc.Iterations)
                .Set(x => x.Spread, doc.Spread)
                .Set(x => x.GameScript, doc.GameScript)
                .Set(x => x.OpponentTeam, doc.OpponentTeam);

            bulkOps.Add(new UpdateOneModel<SimulationResultDocument>(filter, update)
            {
                IsUpsert = true
            });
        }

        if (bulkOps.Count > 0)
            await _collection.BulkWriteAsync(bulkOps, cancellationToken: ct);
    }

    public async Task<SimulationResultDocument?> GetByPlayerAsync(
        string playerId, int season, int week, CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.PlayerId == playerId && x.Season == season && x.Week == week)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<SimulationResultDocument>> GetByWeekAsync(
        int season, int week, CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.Season == season && x.Week == week)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<SimulationResultDocument>> GetByPositionAsync(
        int season, int week, string position, CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.Season == season && x.Week == week && x.Position == position)
            .SortByDescending(x => x.Median)
            .ToListAsync(ct);
    }

    public async Task<SimulationResultDocument?> GetMostRecentBySleeperIdAsync(
        string sleeperPlayerId, int season, CancellationToken ct = default)
    {
        var filter = Builders<SimulationResultDocument>.Filter.And(
            Builders<SimulationResultDocument>.Filter.Eq(x => x.SleeperPlayerId, sleeperPlayerId),
            Builders<SimulationResultDocument>.Filter.Eq(x => x.Season, season));

        return await _collection
            .Find(filter)
            .SortByDescending(x => x.Week)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<SimulationResultDocument>> GetLatestBySleeperIdsAsync(
        IEnumerable<string> sleeperPlayerIds, int season, CancellationToken ct = default)
    {
        var ids = sleeperPlayerIds.ToList();

        var filter = Builders<SimulationResultDocument>.Filter.And(
            Builders<SimulationResultDocument>.Filter.In(x => x.SleeperPlayerId, ids),
            Builders<SimulationResultDocument>.Filter.Eq(x => x.Season, season));

        // Fetch all matching docs — client-side sort because MongoDB driver cannot
        // translate the Week=0 sentinel conditional expression server-side.
        var docs = await _collection
            .Find(filter)
            .ToListAsync(ct);

        // Week=0 (season-average sentinel) wins over any weekly doc; else latest week.
        // int.MaxValue pushes Week=0 to the top of the descending sort.
        var results = docs
            .GroupBy(d => d.SleeperPlayerId)
            .Select(g => g.OrderByDescending(d => d.Week == 0 ? int.MaxValue : d.Week).First())
            .ToList();

        // Offseason fallback: if requested season has no data at all, try season-1.
        if (results.Count == 0 && season >= DateTime.UtcNow.Year && season > 2020)
        {
            logger.LogInformation(
                "No sim data found for season {Season} — falling back to {FallbackSeason}",
                season, season - 1);

            var fallbackFilter = Builders<SimulationResultDocument>.Filter.And(
                Builders<SimulationResultDocument>.Filter.In(x => x.SleeperPlayerId, ids),
                Builders<SimulationResultDocument>.Filter.Eq(x => x.Season, season - 1));

            var fallbackDocs = await _collection
                .Find(fallbackFilter)
                .ToListAsync(ct);

            return fallbackDocs
                .GroupBy(d => d.SleeperPlayerId)
                .Select(g => g.OrderByDescending(d => d.Week == 0 ? int.MaxValue : d.Week).First())
                .ToList();
        }

        return results;
    }
}
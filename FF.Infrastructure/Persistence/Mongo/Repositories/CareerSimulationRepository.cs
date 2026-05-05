using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class CareerSimulationRepository(MongoDbContext context) : ICareerSimulationRepository
{
    private readonly IMongoCollection<CareerSimulationDocument> _collection =
        context.Database.GetCollection<CareerSimulationDocument>("career_simulations");

    public async Task<CareerSimulationDocument?> GetByPlayerIdAsync(
        string sleeperPlayerId,
        CancellationToken ct = default)
    {
        var filter = Builders<CareerSimulationDocument>.Filter
            .Eq(x => x.SleeperPlayerId, sleeperPlayerId);
        return await _collection.Find(filter)
            .FirstOrDefaultAsync(CancellationToken.None);
    }

    public async Task<List<CareerSimulationDocument>> GetByPositionAsync(
        string position,
        CancellationToken ct = default)
    {
        var filter = Builders<CareerSimulationDocument>.Filter
            .Eq(x => x.Position, position);
        return await _collection.Find(filter)
            .SortByDescending(x => x.CareerValueScore)
            .ToListAsync(CancellationToken.None);
    }

    public async Task<List<CareerSimulationDocument>> GetAllBySeasonAsync(
        int season,
        CancellationToken ct = default)
    {
        // Returns the most recent sim per player regardless of season.
        // We don't filter by season because sims may be seeded with a
        // prior-year season value (e.g. 2024 seed data used during off-season).
        // Grouping by SleeperPlayerId and taking the latest ensures we always
        // get exactly one sim per player.
        var all = await _collection
            .Find(Builders<CareerSimulationDocument>.Filter.Empty)
            .ToListAsync(CancellationToken.None);

        // Deduplicate — keep latest per player (highest Season, then newest ComputedAt)
        return all
            .GroupBy(s => s.SleeperPlayerId)
            .Select(g => g
                .OrderByDescending(s => s.Season)
                .ThenByDescending(s => s.ComputedAt)
                .First())
            .ToList();
    }

    public async Task UpsertAsync(
        CareerSimulationDocument document,
        CancellationToken ct = default)
    {
        var filter = Builders<CareerSimulationDocument>.Filter
            .Eq(x => x.SleeperPlayerId, document.SleeperPlayerId);

        var update = Builders<CareerSimulationDocument>.Update
            .Set(x => x.PlayerName, document.PlayerName)
            .Set(x => x.Position, document.Position)
            .Set(x => x.CurrentAge, document.CurrentAge)
            .Set(x => x.Season, document.Season)
            .Set(x => x.CareerPhase, document.CareerPhase)
            .Set(x => x.YearProjections, document.YearProjections)
            .Set(x => x.CareerValueScore, document.CareerValueScore)
            .Set(x => x.PeakYearValue, document.PeakYearValue)
            .Set(x => x.PeakYear, document.PeakYear)
            .Set(x => x.YearsOfPrimeRemaining, document.YearsOfPrimeRemaining)
            .Set(x => x.ComputedAt, document.ComputedAt)
            .Set(x => x.Iterations, document.Iterations)
            .SetOnInsert(x => x.Id, document.Id);

        await _collection.UpdateOneAsync(
            filter,
            update,
            new UpdateOptions { IsUpsert = true },
            CancellationToken.None);
    }

    public async Task UpsertBatchAsync(
        IEnumerable<CareerSimulationDocument> documents,
        CancellationToken ct = default)
    {
        var docList = documents.ToList();
        if (docList.Count == 0) return;

        var writes = docList.Select(document =>
        {
            var filter = Builders<CareerSimulationDocument>.Filter
                .Eq(x => x.SleeperPlayerId, document.SleeperPlayerId);

            var update = Builders<CareerSimulationDocument>.Update
                .Set(x => x.PlayerName, document.PlayerName)
                .Set(x => x.Position, document.Position)
                .Set(x => x.CurrentAge, document.CurrentAge)
                .Set(x => x.Season, document.Season)
                .Set(x => x.CareerPhase, document.CareerPhase)
                .Set(x => x.YearProjections, document.YearProjections)
                .Set(x => x.CareerValueScore, document.CareerValueScore)
                .Set(x => x.PeakYearValue, document.PeakYearValue)
                .Set(x => x.PeakYear, document.PeakYear)
                .Set(x => x.YearsOfPrimeRemaining, document.YearsOfPrimeRemaining)
                .Set(x => x.ComputedAt, document.ComputedAt)
                .Set(x => x.Iterations, document.Iterations)
                .SetOnInsert(x => x.Id, document.Id);

            return new UpdateOneModel<CareerSimulationDocument>(filter, update)
            {
                IsUpsert = true
            };
        }).Cast<WriteModel<CareerSimulationDocument>>().ToList();

        await _collection.BulkWriteAsync(writes, new BulkWriteOptions { IsOrdered = false }, CancellationToken.None);
    }
}
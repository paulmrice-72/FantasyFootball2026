using FF.Application.Interfaces.Repositories;
using FF.Domain.Documents;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class CareerSimulationRepository(MongoDbContext context) : ICareerSimulationRepository
{
    private readonly IMongoCollection<CareerSimulationDocument> _collection =
        context.Database.GetCollection<CareerSimulationDocument>("career_simulations");

    public async Task<CareerSimulationDocument?> GetByPlayerIdAsync(
        string sleeperPlayerId, CancellationToken ct = default)
    {
        var filter = Builders<CareerSimulationDocument>.Filter
            .Eq(x => x.SleeperPlayerId, sleeperPlayerId);
        return await _collection.Find(filter).FirstOrDefaultAsync(ct);
    }

    public async Task<List<CareerSimulationDocument>> GetByPositionAsync(
        string position, CancellationToken ct = default)
    {
        var filter = Builders<CareerSimulationDocument>.Filter
            .Eq(x => x.Position, position);
        return await _collection.Find(filter)
            .SortByDescending(x => x.CareerValueScore)
            .ToListAsync(ct);
    }

    public async Task UpsertAsync(CareerSimulationDocument document, CancellationToken ct = default)
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

        await _collection.UpdateOneAsync(filter, update,
            new UpdateOptions { IsUpsert = true }, ct);
    }

    public async Task UpsertBatchAsync(
        IEnumerable<CareerSimulationDocument> documents, CancellationToken ct = default)
    {
        var tasks = documents.Select(d => UpsertAsync(d, ct));
        await Task.WhenAll(tasks);
    }
}
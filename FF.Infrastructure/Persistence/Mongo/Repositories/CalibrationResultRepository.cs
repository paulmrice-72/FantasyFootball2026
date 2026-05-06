// FF.Infrastructure/Persistence/Mongo/Repositories/CalibrationResultRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class CalibrationResultRepository(MongoDbContext context) : ICalibrationResultRepository
{
    private readonly IMongoCollection<CalibrationResultDocument> _collection =
        context.Database.GetCollection<CalibrationResultDocument>("calibration_results");

    public async Task InsertAsync(CalibrationResultDocument document, CancellationToken ct = default)
        => await _collection.InsertOneAsync(document, cancellationToken: ct);

    public async Task<List<CalibrationResultDocument>> GetRecentAsync(int count = 10, CancellationToken ct = default)
        => await _collection.Find(FilterDefinition<CalibrationResultDocument>.Empty)
            .SortByDescending(x => x.RunAt)
            .Limit(count)
            .ToListAsync(ct);

    public async Task<CalibrationResultDocument?> GetLatestAsync(CancellationToken ct = default)
        => await _collection.Find(FilterDefinition<CalibrationResultDocument>.Empty)
            .SortByDescending(x => x.RunAt)
            .FirstOrDefaultAsync(ct);
}
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class PickValueRepository(
    MongoDbContext db,
    ILogger<PickValueRepository> logger) : IPickValueRepository
{
    private readonly IMongoCollection<PickValueDocument> _collection =
        db.GetCollection<PickValueDocument>("pick_values");

    public async Task<PickValueDocument?> GetAsync(
        int round, string tier, int year, CancellationToken ct = default)
    {
        return await _collection
            .Find(x => x.Round == round && x.Tier == tier && x.Year == year)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<IReadOnlyList<PickValueDocument>> GetAllAsync(
        CancellationToken ct = default)
    {
        return await _collection
            .Find(FilterDefinition<PickValueDocument>.Empty)
            .SortBy(x => x.Year)
            .ThenBy(x => x.Round)
            .ToListAsync(ct);
    }
}
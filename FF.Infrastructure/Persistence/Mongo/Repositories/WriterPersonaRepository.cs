// FF.Infrastructure/Persistence/Mongo/Repositories/WriterPersonaRepository.cs
using FF.Application.Interfaces.Persistence;
using FF.Domain.Documents;
using FF.Infrastructure.Persistence.Mongo;
using MongoDB.Driver;

namespace FF.Infrastructure.Persistence.Mongo.Repositories;

public class WriterPersonaRepository(MongoDbContext context) : IWriterPersonaRepository
{
    private readonly IMongoCollection<WriterPersonaDocument> _collection =
        context.GetCollection<WriterPersonaDocument>("writer_personas");

    public async Task<IReadOnlyList<WriterPersonaDocument>> GetAllActiveAsync(
        CancellationToken cancellationToken = default)
    {
        var filter = Builders<WriterPersonaDocument>.Filter.Eq(x => x.IsActive, true);
        return await _collection
            .Find(filter)
            .SortBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<WriterPersonaDocument?> GetByIdAsync(
        string id, CancellationToken cancellationToken = default)
    {
        var filter = Builders<WriterPersonaDocument>.Filter.Eq(x => x.Id, id);
        return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task UpsertAsync(
        WriterPersonaDocument document, CancellationToken cancellationToken = default)
    {
        document.UpdatedAt = DateTime.UtcNow;
        var filter = Builders<WriterPersonaDocument>.Filter.Eq(x => x.Id, document.Id);
        var options = new ReplaceOptions { IsUpsert = true };
        await _collection.ReplaceOneAsync(filter, document, options, cancellationToken);
    }

    public async Task AddFeedbackAsync(
    string personaId, WriterFeedbackEntry entry, CancellationToken ct = default)
    {
        var filter = Builders<WriterPersonaDocument>.Filter.Eq(x => x.Id, personaId);
        var update = Builders<WriterPersonaDocument>.Update
            .Push(x => x.PersistentFeedback, entry)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);
        await _collection.UpdateOneAsync(filter, update, cancellationToken: ct);
    }
}
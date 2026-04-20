// FF.Application/Interfaces/Persistence/IWriterPersonaRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IWriterPersonaRepository
{
    Task<IReadOnlyList<WriterPersonaDocument>> GetAllActiveAsync(CancellationToken ct = default);
    Task<WriterPersonaDocument?> GetByIdAsync(string id, CancellationToken ct = default);
    Task UpsertAsync(WriterPersonaDocument document, CancellationToken ct = default);
    Task AddFeedbackAsync(string personaId, WriterFeedbackEntry entry, CancellationToken ct = default);
}
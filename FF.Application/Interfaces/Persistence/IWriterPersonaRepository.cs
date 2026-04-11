// FF.Application/Interfaces/Persistence/IWriterPersonaRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IWriterPersonaRepository
{
    Task<IReadOnlyList<WriterPersonaDocument>> GetAllActiveAsync(
        CancellationToken cancellationToken = default);

    Task<WriterPersonaDocument?> GetByIdAsync(
        string id, CancellationToken cancellationToken = default);

    Task UpsertAsync(
        WriterPersonaDocument document, CancellationToken cancellationToken = default);
}
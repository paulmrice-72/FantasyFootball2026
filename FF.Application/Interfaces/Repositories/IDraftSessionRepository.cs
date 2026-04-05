// FF.Application/Interfaces/Repositories/IDraftSessionRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Repositories;

public interface IDraftSessionRepository
{
    Task<DraftSessionDocument?> GetByIdAsync(string id, CancellationToken ct = default);
    Task<DraftSessionDocument?> GetActiveByUserAndLeagueAsync(
        string userId, string leagueId, CancellationToken ct = default);
    Task<List<DraftSessionDocument>> GetByUserIdAsync(
        string userId, CancellationToken ct = default);
    Task InsertAsync(DraftSessionDocument document, CancellationToken ct = default);
    Task UpdateAsync(DraftSessionDocument document, CancellationToken ct = default);
}
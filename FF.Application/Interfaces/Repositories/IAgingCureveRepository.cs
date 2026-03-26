using FF.Domain.Documents;

namespace FF.Application.Interfaces.Repositories;

public interface IAgingCurveRepository
{
    Task<AgingCurveDocument?> GetByPositionAsync(string position, CancellationToken ct = default);
    Task<List<AgingCurveDocument>> GetAllAsync(CancellationToken ct = default);
    Task UpsertAsync(AgingCurveDocument document, CancellationToken ct = default);
}
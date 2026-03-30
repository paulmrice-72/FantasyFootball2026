using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IInjuryAlertRepository
{
    Task UpsertBatchAsync(IEnumerable<InjuryAlertDocument> alerts, CancellationToken ct = default);
    Task<IReadOnlyList<InjuryAlertDocument>> GetActiveAlertsAsync(string? position = null, CancellationToken ct = default);
    Task DeleteAllAsync(CancellationToken ct = default);
}
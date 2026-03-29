// FF.Application/Interfaces/Persistence/IEmergenceAlertRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IEmergenceAlertRepository
{
    Task UpsertBatchAsync(
        IEnumerable<EmergenceAlertDocument> alerts,
        CancellationToken ct = default);

    Task<IReadOnlyList<EmergenceAlertDocument>> GetBySeasonWeekAsync(
        int season,
        int week,
        string? position = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<EmergenceAlertDocument>> GetLatestBySeasonAsync(
        int season,
        string? position = null,
        CancellationToken ct = default);
}
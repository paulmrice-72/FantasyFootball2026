// FF.Application/Interfaces/Persistence/IUsageMetricsRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IUsageMetricsRepository
{
    Task<PlayerUsageMetricsDocument?> GetByPlayerSeasonAsync(
        string playerId,
        int season,
        CancellationToken ct = default);

    Task UpsertAsync(
        PlayerUsageMetricsDocument metrics,
        CancellationToken ct = default);

    Task<IReadOnlyList<PlayerUsageMetricsDocument>> GetBySeasonAsync(
        int season,
        string? position = null,
        CancellationToken ct = default);
}
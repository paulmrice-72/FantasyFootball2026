// FF.Application/Interfaces/Persistence/IVorpRecommendationRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IVorpRecommendationRepository
{
    Task UpsertBatchAsync(
        IEnumerable<VorpRecommendationDocument> recommendations,
        CancellationToken ct = default);

    /// <summary>
    /// FAN-118: league-scoped. VORP without a league is not a meaningful number —
    /// both baselines depend on the league's roster configuration and its rostered set.
    /// </summary>
    Task<IReadOnlyList<VorpRecommendationDocument>> GetByWeekAsync(
        string sleeperLeagueId,
        int season,
        int week,
        string? position = null,
        int top = 50,
        CancellationToken ct = default);

    /// <summary>Removes a league's rows for one week, so a recompute cannot leave orphans behind.</summary>
    Task DeleteForWeekAsync(
        string sleeperLeagueId,
        int season,
        int week,
        CancellationToken ct = default);
}

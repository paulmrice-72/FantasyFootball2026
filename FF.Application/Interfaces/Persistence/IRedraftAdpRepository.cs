// FF.Application/Interfaces/Persistence/IRedraftAdpRepository.cs
using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

/// <summary>
/// Reads the redraftAdpCache collection populated by SyncRedraftAdpJob
/// (live FFC consensus ADP). FIX-PRESEASON-001 (2026-08-27): added as the
/// preseason redraft ranking source — ADP already reflects real 2026 drafts
/// happening industry-wide, so it naturally covers rookies, unlike the
/// Week-1 simulation pipeline which needs this season's own game logs.
/// </summary>
public interface IRedraftAdpRepository
{
    Task<List<RedraftAdpCacheDocument>> GetBySeasonAsync(
        int season,
        string scoringFormat = "ppr",
        CancellationToken cancellationToken = default);
}

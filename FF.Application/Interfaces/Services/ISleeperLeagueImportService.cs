// FF.Application/Interfaces/Services/ISleeperLeagueImportService.cs
//
// Interface for the Sleeper league import service.
// Lives in FF.Application so the command handler can depend on it
// without knowing anything about Sleeper, HTTP, or EF Core.
// The implementation lives in FF.Infrastructure.

using FF.Application.Leagues.Commands.ImportLeague;

namespace FF.Application.Interfaces.Services;

public interface ISleeperLeagueImportService
{
    /// <summary>
    /// Imports a complete Sleeper league into the local database.
    /// Fetches league settings, all rosters, all owners, and 2 seasons
    /// of transaction history. All writes are idempotent upserts.
    /// </summary>
    Task<ImportLeagueResult> ImportLeagueAsync(
        string sleeperLeagueId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Syncs an already-imported league with the latest data from Sleeper.
    /// Updates rosters, ownership, and adds any new transactions.
    /// Called by the weekly Hangfire sync job.
    /// </summary>
    Task SyncLeagueAsync(
        string sleeperLeagueId,
        CancellationToken cancellationToken = default);
}

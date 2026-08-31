using FF.Domain.Documents;

namespace FF.Application.Interfaces.Persistence;

public interface IRosterPlayerRepository
{
    Task UpsertAsync(RosterPlayerDocument document, CancellationToken ct = default);
    Task UpsertBatchAsync(IEnumerable<RosterPlayerDocument> documents, CancellationToken ct = default);

    Task<RosterPlayerDocument?> GetByRosterIdAsync(
        string sleeperRosterId, string sleeperLeagueId, CancellationToken ct = default);

    Task<IReadOnlyList<RosterPlayerDocument>> GetByLeagueAsync(
        string sleeperLeagueId, CancellationToken ct = default);

    /// <summary>Find the roster containing a specific player in a league.</summary>
    Task<RosterPlayerDocument?> GetByPlayerIdAsync(
        string sleeperPlayerId, string sleeperLeagueId, CancellationToken ct = default);
    Task<RosterPlayerDocument?> GetBySleeperUserIdAsync(
    string sleeperUserId, string sleeperLeagueId, CancellationToken ct = default);

    /// <summary>
    /// Deletes any roster documents for this league whose SleeperRosterId is
    /// NOT in currentRosterIds — i.e. rosters Sleeper no longer returns for
    /// this league (a team removed, or roster_ids renumbered after a season
    /// reset/roster-count change). Import/sync only ever upserted rosters
    /// Sleeper currently returns and never pruned the rest, so old rosters
    /// stuck around forever — same "upsert but never pruned" shape as the
    /// SyncRedraftAdpJob zombie-cache fix (FAN-105). Returns the number
    /// deleted.
    /// </summary>
    Task<long> DeleteStaleRostersAsync(
        string sleeperLeagueId, IEnumerable<string> currentRosterIds, CancellationToken ct = default);
}
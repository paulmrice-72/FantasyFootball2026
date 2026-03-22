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
}
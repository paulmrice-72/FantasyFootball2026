using FF.Domain.Entities;

namespace FF.Application.Interfaces.Persistence;

public interface IRosterRepository : IRepository<Roster>
{
    Task<IReadOnlyList<Roster>> GetByLeagueIdAsync(Guid leagueId, CancellationToken cancellationToken = default);
    Task<Roster?> GetBySleeperRosterIdAsync(string sleeperRosterId, CancellationToken cancellationToken = default);
}